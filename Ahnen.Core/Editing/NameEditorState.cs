using Ahnen.Core.Models;

namespace Ahnen.Core.Editing;

/// <summary>Keeps ordered name records separate from an unrecorded type's draft.</summary>
public sealed class NameEditorState
{
    private List<PersonName> _names = [];
    private List<PersonName> _baseline = [];
    private PersonName _draft = new();

    public NameType SelectedType { get; private set; } = NameType.BIRTH;
    public int? SelectedIndex { get; private set; }
    public PersonName Current => SelectedIndex is { } index ? _names[index] : _draft;
    public bool IsDraft => SelectedIndex is null;
    public bool CanRemove => SelectedIndex is not null && _names.Count > 1;
    public bool HasChanges => !NamesEqual(_names, _baseline) || IsDraft && HasContent(_draft);

    public IReadOnlyList<int> Entries => Enumerable.Range(0, _names.Count)
        .Where(i => _names[i].Type == SelectedType).ToArray();

    public int Count(NameType type) => _names.Count(n => n.Type == type);
    public PersonName Entry(int index) => _names[index];

    public void Load(IReadOnlyList<PersonName> names, bool preserveSelection = false)
    {
        var previousIndex = SelectedIndex;
        _names = names.Select(Clone).ToList();
        _baseline = names.Select(Clone).ToList();
        if (!preserveSelection)
            SelectedType = _names.FirstOrDefault()?.Type ?? NameType.BIRTH;
        SelectedIndex = preserveSelection && previousIndex is { } index &&
                        index < _names.Count && _names[index].Type == SelectedType
            ? index
            : Enumerable.Range(0, _names.Count).Cast<int?>()
                .FirstOrDefault(i => _names[i!.Value].Type == SelectedType);
        _draft = new PersonName { Type = SelectedType };
    }

    public void SelectType(NameType type)
    {
        EnsureSaved();
        SelectedType = type;
        SelectedIndex = Enumerable.Range(0, _names.Count).Cast<int?>()
            .FirstOrDefault(i => _names[i!.Value].Type == type);
        _draft = new PersonName { Type = type };
    }

    public void SelectEntry(int index)
    {
        EnsureSaved();
        if (index < 0 || index >= _names.Count || _names[index].Type != SelectedType)
            throw new InvalidOperationException("The selected name no longer exists.");
        SelectedIndex = index;
    }

    public void AddAnother()
    {
        EnsureSaved();
        SelectedIndex = null;
        _draft = new PersonName { Type = SelectedType };
    }

    public void RemoveSelected()
    {
        if (!CanRemove) return;
        var index = SelectedIndex!.Value;
        _names.RemoveAt(index);
        SelectedIndex = Math.Min(index, _names.Count - 1);
        SelectedType = Current.Type;
        _draft = new PersonName { Type = SelectedType };
    }

    public IReadOnlyList<PersonName> PendingNames()
    {
        var names = _names.Select(Clone).ToList();
        if (IsDraft && HasContent(_draft)) names.Add(Clone(_draft));
        return names;
    }

    public bool MatchesBaseline(IReadOnlyList<PersonName> names) => NamesEqual(_baseline, names);
    public IReadOnlyList<PersonName> BaselineNames() => _baseline.Select(Clone).ToList();

    public void AcceptSave()
    {
        if (IsDraft && HasContent(_draft))
        {
            _names.Add(Clone(_draft));
            SelectedIndex = _names.Count - 1;
        }

        _baseline = _names.Select(Clone).ToList();
    }

    private void EnsureSaved()
    {
        if (HasChanges)
            throw new InvalidOperationException("Save pending name changes before changing selection.");
    }

    private static bool HasContent(PersonName name) =>
        name.Given.Length > 0 || name.Surname.Length > 0 ||
        !string.IsNullOrEmpty(name.Prefix) || !string.IsNullOrEmpty(name.Suffix) ||
        !string.IsNullOrEmpty(name.Nickname);

    private static PersonName Clone(PersonName name) => new()
    {
        Type = name.Type, Given = name.Given, Surname = name.Surname,
        Prefix = name.Prefix, Suffix = name.Suffix, Nickname = name.Nickname
    };

    private static bool NamesEqual(IReadOnlyList<PersonName> left, IReadOnlyList<PersonName> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Type == pair.Second.Type &&
            pair.First.Given == pair.Second.Given && pair.First.Surname == pair.Second.Surname &&
            pair.First.Prefix == pair.Second.Prefix && pair.First.Suffix == pair.Second.Suffix &&
            pair.First.Nickname == pair.Second.Nickname);
}