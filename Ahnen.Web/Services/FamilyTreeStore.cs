using System.Text.Json;
using Ahnen.Web.Models;

namespace Ahnen.Web.Services;

/// <summary>
/// Owns the family tree data: loads/saves the JSON file (atomic write + rolling
/// backup), provides CRUD and structural relationship operations, validation,
/// and a one-step undo snapshot. Thread-safe via a single lock (single-user app).
/// </summary>
public class FamilyTreeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _dataFile;
    private FamilyTreeData _data = new();
    private string? _undoSnapshot;

    public string DataDirectory { get; }
    public string PhotosDirectory { get; }

    /// <summary>Raised after any mutation is persisted; pages subscribe to refresh.</summary>
    public event Action? Changed;

    public FamilyTreeStore(IConfiguration config, IWebHostEnvironment env)
    {
        DataDirectory = Environment.GetEnvironmentVariable("FAMILYTREE_DATA_DIR")
            ?? config["DataDirectory"]
            ?? Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(DataDirectory);
        PhotosDirectory = Path.Combine(DataDirectory, "photos");
        Directory.CreateDirectory(PhotosDirectory);
        _dataFile = Path.Combine(DataDirectory, "familytree.json");
        Load(env);
    }

    private void Load(IWebHostEnvironment env)
    {
        if (File.Exists(_dataFile))
        {
            _data = JsonSerializer.Deserialize<FamilyTreeData>(File.ReadAllText(_dataFile), JsonOptions) ?? new();
            return;
        }
        // First run: copy the seed file if present.
        var seed = Path.Combine(env.ContentRootPath, "seed", "familytree.json");
        if (File.Exists(seed))
        {
            _data = JsonSerializer.Deserialize<FamilyTreeData>(File.ReadAllText(seed), JsonOptions) ?? new();
            var seedPhotos = Path.Combine(env.ContentRootPath, "seed", "photos");
            if (Directory.Exists(seedPhotos))
                foreach (var f in Directory.GetFiles(seedPhotos))
                    File.Copy(f, Path.Combine(PhotosDirectory, Path.GetFileName(f)), overwrite: false);
        }
        Save();
    }

    private void Save()
    {
        GedcomValidator.ValidateLegal(_data); // GEDCOM compliance gate: never persist illegal values
        var json = JsonSerializer.Serialize(_data, JsonOptions);
        var tmp = _dataFile + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_dataFile))
            File.Copy(_dataFile, _dataFile + ".bak", overwrite: true);
        File.Move(tmp, _dataFile, overwrite: true);
    }

    private void Snapshot() => _undoSnapshot = JsonSerializer.Serialize(_data, JsonOptions);

    /// <summary>Restores the state before the most recent mutation. Returns false when nothing to undo.</summary>
    public bool Undo()
    {
        lock (_gate)
        {
            if (_undoSnapshot is null) return false;
            _data = JsonSerializer.Deserialize<FamilyTreeData>(_undoSnapshot, JsonOptions)!;
            _undoSnapshot = null;
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    private T Mutate<T>(Func<FamilyTreeData, T> action)
    {
        T result;
        lock (_gate)
        {
            Snapshot();
            result = action(_data);
            Save();
        }
        Changed?.Invoke();
        return result;
    }

    // ---- queries (all return deep copies so UI edits never mutate internal state) ----

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    public IReadOnlyList<Person> Persons { get { lock (_gate) return _data.Persons.Select(Clone).ToList(); } }
    public IReadOnlyList<Family> Families { get { lock (_gate) return _data.Families.Select(Clone).ToList(); } }
    public string? RootPersonId { get { lock (_gate) return _data.RootPersonId; } }

    public Person? GetPerson(string id)
    {
        lock (_gate)
        {
            var p = _data.Persons.FirstOrDefault(p => p.Id == id);
            return p is null ? null : Clone(p);
        }
    }

    public Family? GetFamily(string id)
    {
        lock (_gate)
        {
            var f = _data.Families.FirstOrDefault(f => f.Id == id);
            return f is null ? null : Clone(f);
        }
    }

    public IReadOnlyList<Family> FamiliesAsPartner(string personId)
    {
        lock (_gate) return _data.Families.Where(f => f.PartnerIds.Contains(personId)).Select(Clone).ToList();
    }

    public IReadOnlyList<Family> FamiliesAsChild(string personId)
    {
        lock (_gate) return _data.Families.Where(f => f.ChildRefs.Any(c => c.PersonId == personId)).Select(Clone).ToList();
    }

    public IReadOnlyList<Person> ParentsOf(string personId) =>
        FamiliesAsChild(personId).SelectMany(f => f.PartnerIds)
            .Distinct().Select(GetPerson).Where(p => p is not null).Cast<Person>().ToList();

    public IReadOnlyList<Person> ChildrenOf(string personId) =>
        FamiliesAsPartner(personId).SelectMany(f => f.ChildRefs.Select(c => c.PersonId))
            .Distinct().Select(GetPerson).Where(p => p is not null).Cast<Person>().ToList();

    /// <summary>All ancestor IDs of a person (used for cycle prevention).</summary>
    public HashSet<string> AncestorIds(string personId)
    {
        var result = new HashSet<string>();
        var queue = new Queue<string>([personId]);
        while (queue.Count > 0)
        {
            foreach (var parent in ParentsOf(queue.Dequeue()))
                if (result.Add(parent.Id))
                    queue.Enqueue(parent.Id);
        }
        return result;
    }

    // ---- person CRUD ---------------------------------------------------

    public Person AddPerson(string given, string surname, Sex sex = Sex.U) =>
        Mutate(d =>
        {
            var person = new Person
            {
                Id = NextId(d.Persons.Select(p => p.Id), 'I'),
                Sex = sex,
                Names = [new PersonName { Given = given, Surname = surname }]
            };
            d.Persons.Add(person);
            d.RootPersonId ??= person.Id;
            return person;
        });

    public void UpdatePerson(Person person) =>
        Mutate(d =>
        {
            var i = d.Persons.FindIndex(p => p.Id == person.Id);
            if (i < 0) throw new InvalidOperationException($"Unknown person {person.Id}");
            d.Persons[i] = person;
            return true;
        });

    /// <summary>Deletes a person and cleans every reference. Returns removed photo files for disk cleanup.</summary>
    public IReadOnlyList<string> DeletePerson(string personId) =>
        Mutate(d =>
        {
            var person = d.Persons.FirstOrDefault(p => p.Id == personId)
                ?? throw new InvalidOperationException($"Unknown person {personId}");
            var photos = person.Media.Select(m => m.File).ToList();
            d.Persons.Remove(person);
            foreach (var family in d.Families.ToList())
            {
                family.PartnerIds.Remove(personId);
                family.ChildRefs.RemoveAll(c => c.PersonId == personId);
                // A childless family needs two partners to still carry meaning.
                if (family.ChildRefs.Count == 0 && family.PartnerIds.Count <= 1)
                    d.Families.Remove(family);
            }
            if (d.RootPersonId == personId)
                d.RootPersonId = d.Persons.FirstOrDefault()?.Id;
            return (IReadOnlyList<string>)photos;
        });

    public void SetRootPerson(string personId) =>
        Mutate(d =>
        {
            if (d.Persons.All(p => p.Id != personId))
                throw new InvalidOperationException($"Unknown person {personId}");
            d.RootPersonId = personId;
            return true;
        });

    // ---- relationship operations ---------------------------------------

    /// <summary>Links two persons as partners, creating a family. No-op when already partners.</summary>
    public Family AddPartner(string personId, string partnerId) =>
        Mutate(d =>
        {
            if (personId == partnerId)
                throw new InvalidOperationException("A person cannot be their own partner.");
            var existing = d.Families.FirstOrDefault(f =>
                f.PartnerIds.Contains(personId) && f.PartnerIds.Contains(partnerId));
            if (existing is not null) return existing;

            // Reuse a single-partner family of either person before creating a new one.
            var family = d.Families.FirstOrDefault(f =>
                f.PartnerIds.Count == 1 && (f.PartnerIds[0] == personId || f.PartnerIds[0] == partnerId));
            if (family is null)
            {
                family = new Family { Id = NextId(d.Families.Select(f => f.Id), 'F') };
                d.Families.Add(family);
            }
            if (!family.PartnerIds.Contains(personId)) family.PartnerIds.Add(personId);
            if (!family.PartnerIds.Contains(partnerId)) family.PartnerIds.Add(partnerId);
            return family;
        });

    /// <summary>Adds a child to the given parent (and optionally their partner) with cycle prevention.</summary>
    public Family AddChild(string parentId, string childId, Pedigree pedigree = Pedigree.BIRTH, string? familyId = null) =>
        Mutate(d =>
        {
            if (parentId == childId)
                throw new InvalidOperationException("A person cannot be their own child.");
            if (AncestorIdsUnsafe(d, parentId).Contains(childId))
                throw new InvalidOperationException("This link would make someone their own ancestor.");

            Family? family = familyId is not null
                ? d.Families.FirstOrDefault(f => f.Id == familyId)
                : d.Families.FirstOrDefault(f => f.PartnerIds.Contains(parentId));
            if (family is null)
            {
                family = new Family { Id = NextId(d.Families.Select(f => f.Id), 'F'), PartnerIds = [parentId] };
                d.Families.Add(family);
            }
            else if (!family.PartnerIds.Contains(parentId))
            {
                throw new InvalidOperationException($"{parentId} is not a partner in family {family.Id}.");
            }
            if (family.ChildRefs.All(c => c.PersonId != childId))
                family.ChildRefs.Add(new ChildRef { PersonId = childId, Pedigree = pedigree });
            return family;
        });

    /// <summary>Adds a parent to a child: attaches to the child's existing parent family or creates one.</summary>
    public Family AddParent(string childId, string parentId, Pedigree pedigree = Pedigree.BIRTH) =>
        Mutate(d =>
        {
            if (parentId == childId)
                throw new InvalidOperationException("A person cannot be their own parent.");
            if (AncestorIdsUnsafe(d, parentId).Contains(childId))
                throw new InvalidOperationException("This link would make someone their own ancestor.");

            // Prefer a parent family of the child that still has room (max 2 partners).
            var family = d.Families.FirstOrDefault(f =>
                f.ChildRefs.Any(c => c.PersonId == childId) &&
                (f.PartnerIds.Contains(parentId) || f.PartnerIds.Count < 2));
            if (family is null)
            {
                family = new Family { Id = NextId(d.Families.Select(f => f.Id), 'F') };
                family.ChildRefs.Add(new ChildRef { PersonId = childId, Pedigree = pedigree });
                d.Families.Add(family);
            }
            if (!family.PartnerIds.Contains(parentId)) family.PartnerIds.Add(parentId);
            return family;
        });

    /// <summary>Removes a partner from a family; deletes the family when it becomes empty.</summary>
    public void RemovePartner(string familyId, string personId) =>
        Mutate(d =>
        {
            var family = d.Families.FirstOrDefault(f => f.Id == familyId)
                ?? throw new InvalidOperationException($"Unknown family {familyId}");
            family.PartnerIds.Remove(personId);
            if (family.ChildRefs.Count == 0 && family.PartnerIds.Count <= 1)
                d.Families.Remove(family);
            return true;
        });

    /// <summary>Removes a child from a family; deletes the family when it loses its meaning.</summary>
    public void RemoveChild(string familyId, string childId) =>
        Mutate(d =>
        {
            var family = d.Families.FirstOrDefault(f => f.Id == familyId)
                ?? throw new InvalidOperationException($"Unknown family {familyId}");
            family.ChildRefs.RemoveAll(c => c.PersonId == childId);
            if (family.ChildRefs.Count == 0 && family.PartnerIds.Count <= 1)
                d.Families.Remove(family);
            return true;
        });

    public void UpdateFamily(Family family) =>
        Mutate(d =>
        {
            var i = d.Families.FindIndex(f => f.Id == family.Id);
            if (i < 0) throw new InvalidOperationException($"Unknown family {family.Id}");
            d.Families[i] = family;
            return true;
        });

    public void SetChildPedigree(string familyId, string childId, Pedigree pedigree) =>
        Mutate(d =>
        {
            var child = d.Families.FirstOrDefault(f => f.Id == familyId)?
                .ChildRefs.FirstOrDefault(c => c.PersonId == childId)
                ?? throw new InvalidOperationException($"Child {childId} not in family {familyId}");
            child.Pedigree = pedigree;
            return true;
        });

    // ---- helpers --------------------------------------------------------

    private static HashSet<string> AncestorIdsUnsafe(FamilyTreeData d, string personId)
    {
        var result = new HashSet<string>();
        var queue = new Queue<string>([personId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var parent in d.Families
                         .Where(f => f.ChildRefs.Any(c => c.PersonId == current))
                         .SelectMany(f => f.PartnerIds))
                if (result.Add(parent))
                    queue.Enqueue(parent);
        }
        return result;
    }

    private static string NextId(IEnumerable<string> existing, char prefix)
    {
        var max = existing
            .Where(id => id.Length > 1 && id[0] == prefix && int.TryParse(id[1..], out _))
            .Select(id => int.Parse(id[1..]))
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{max + 1}";
    }
}
