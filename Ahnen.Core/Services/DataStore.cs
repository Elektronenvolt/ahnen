using System.Text.Json;
using Ahnen.Core.Helpers;
using Ahnen.Core.Models;

namespace Ahnen.Core.Services;

/// <summary>Host-supplied storage location. The web host and the desktop host each provide their own.</summary>
/// <param name="DataDirectory">Writable folder holding the JSON document and the <c>photos</c> subfolder.</param>
/// <param name="DataFileName">File name only (no path) of the JSON document inside <paramref name="DataDirectory"/>.</param>
public sealed record DataStoreOptions(string DataDirectory, string? DataFileName);

/// <summary>
/// Owns the family tree data
/// backup), provides CRUD and structural relationship operations, validation,
/// and a one-step undo snapshot. Thread-safe via a single lock (single-user app).
/// </summary>
public class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string? _dataFile;
    private FamilyTreeData _data = new();
    private string? _undoSnapshot;

    public string DataDirectory { get; }
    public string PhotosDirectory { get; }
    public bool CanEdit { get; }

    /// <summary>Raised after any mutation is persisted; pages subscribe to refresh.</summary>
    public event Action? Changed;

    public DataStore(DataStoreOptions options, ILogger<DataStore> logger)
    {
        DataDirectory = options.DataDirectory;
        PhotosDirectory = Path.Combine(DataDirectory, "photos");
        Directory.CreateDirectory(PhotosDirectory);
        var fileName = options.DataFileName;
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.IndexOfAny(['/', '\\', '<', '>', ':', '"', '|', '?', '*']) >= 0)
        {
            logger.LogError("DataFileName must be configured as a filename without a path. No data loaded; editing is disabled.");
            return;
        }

        _dataFile = Path.Combine(DataDirectory, fileName);
        try
        {
            _data = JsonSerializer.Deserialize<FamilyTreeData>(File.ReadAllText(_dataFile), JsonOptions)
                ?? throw new JsonException("The data file must contain a family tree object.");
            CanEdit = true;
        }
        catch (FileNotFoundException)
        {
            logger.LogError("Data file {DataFile} was not found. No data loaded; editing is disabled.", _dataFile);
        }
    }

    public void EnsureCanEdit()
    {
        if (!CanEdit)
            throw new InvalidOperationException("No data file is loaded. Editing is disabled; correct DataFileName and restart the application.");
    }

    private void Save()
    {
        EnsureCanEdit();
        GedcomValidator.ValidateLegal(_data); // GEDCOM compliance gate: never persist illegal values
        var json = JsonSerializer.Serialize(_data, JsonOptions);
        var tmp = _dataFile + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            if (File.Exists(_dataFile))
                File.Copy(_dataFile, _dataFile + ".bak", overwrite: true);
            File.Move(tmp, _dataFile!, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    private string Snapshot() => JsonSerializer.Serialize(_data, JsonOptions);

    /// <summary>Restores the state before the most recent mutation. Returns false when nothing to undo.</summary>
    public bool Undo()
    {
        lock (_gate)
        {
            EnsureCanEdit();
            if (_undoSnapshot is null) return false;
            var before = _data;
            var snapshot = _undoSnapshot;
            _data = JsonSerializer.Deserialize<FamilyTreeData>(snapshot, JsonOptions)!;
            _undoSnapshot = null;
            try
            {
                Save();
            }
            catch
            {
                _data = before;
                _undoSnapshot = snapshot;
                throw;
            }
        }
        Changed?.Invoke();
        return true;
    }

    private T Mutate<T>(Func<FamilyTreeData, T> action)
    {
        T result;
        lock (_gate)
        {
            EnsureCanEdit();
            // A failed action or save must not leave memory ahead of what is on disk.
            var before = Snapshot();
            var previousUndo = _undoSnapshot;
            try
            {
                result = action(_data);
                _undoSnapshot = before;
                Save();
            }
            catch
            {
                _data = JsonSerializer.Deserialize<FamilyTreeData>(before, JsonOptions)!;
                _undoSnapshot = previousUndo;
                throw;
            }
        }
        Changed?.Invoke();
        return result;
    }

    // ---- queries (all return deep copies so UI edits never mutate internal state) ----

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    public IReadOnlyList<Person> Persons { get { lock (_gate) return Clone(_data.Persons); } }
    public IReadOnlyList<Family> Families { get { lock (_gate) return Clone(_data.Families); } }
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
        PersonsByIds(FamiliesAsChild(personId).SelectMany(f => f.PartnerIds));

    public IReadOnlyList<Person> PartnersOf(string personId) =>
        PersonsByIds(FamiliesAsPartner(personId).SelectMany(f => f.PartnerIds).Where(id => id != personId));

    public IReadOnlyList<Person> ChildrenOf(string personId) =>
        PersonsByIds(FamiliesAsPartner(personId).SelectMany(f => f.ChildRefs.Select(c => c.PersonId)));

    private IReadOnlyList<Person> PersonsByIds(IEnumerable<string> ids) =>
        ids.Distinct().Select(GetPerson).OfType<Person>().ToList();

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

    public void UpdatePersonFacts(Person person) =>
        Mutate(d =>
        {
            var index = d.Persons.FindIndex(p => p.Id == person.Id);
            if (index < 0) throw new InvalidOperationException($"Unknown person {person.Id}");
            var updated = Clone(person);
            updated.Names = Clone(d.Persons[index].Names);
            d.Persons[index] = updated;
            return true;
        });

    public bool UpdateNames(string personId, IReadOnlyList<PersonName> names,
        IReadOnlyList<PersonName>? expectedNames = null)
    {
        lock (_gate)
        {
            EnsureCanEdit();
            var person = _data.Persons.FirstOrDefault(p => p.Id == personId)
                ?? throw new InvalidOperationException($"Unknown person {personId}");
            var currentJson = JsonSerializer.Serialize(person.Names, JsonOptions);
            if (expectedNames is not null &&
                currentJson != JsonSerializer.Serialize(expectedNames, JsonOptions))
                throw new InvalidOperationException("Names changed since they were loaded. Reload before saving.");
            if (currentJson == JsonSerializer.Serialize(names, JsonOptions)) return false;

            var candidate = Clone(_data);
            candidate.Persons.Single(p => p.Id == personId).Names = Clone(names.ToList());
            GedcomValidator.ValidateLegal(candidate);
            var before = _data;
            _data = candidate;
            try
            {
                Save();
            }
            catch
            {
                _data = before;
                throw;
            }
            _undoSnapshot = JsonSerializer.Serialize(before, JsonOptions);
        }
        Changed?.Invoke();
        return true;
    }

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
                d.PruneIfObsolete(family);
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
            if (d.AncestorsOf(parentId).Contains(childId))
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
            if (d.AncestorsOf(parentId).Contains(childId))
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
            d.PruneIfObsolete(family);
            return true;
        });

    /// <summary>Removes a child from a family; deletes the family when it loses its meaning.</summary>
    public void RemoveChild(string familyId, string childId) =>
        Mutate(d =>
        {
            var family = d.Families.FirstOrDefault(f => f.Id == familyId)
                ?? throw new InvalidOperationException($"Unknown family {familyId}");
            family.ChildRefs.RemoveAll(c => c.PersonId == childId);
            d.PruneIfObsolete(family);
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
