using System.IO;
using System.Text;
using System.Windows;
using Ahnen.Core.Models;
using Ahnen.Core.Services;
using Ahnen.Desktop.Localization;
using Ahnen.Desktop.Services;
using Ahnen.Desktop.Visualization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Ahnen.Desktop.ViewModels;

public sealed record TreeChoice<T>(T Value, string Label);

public sealed partial class TreePageViewModel : PageViewModel, IDisposable
{
    private readonly Localizer _localizer;
    private readonly DocumentSession _document;
    private readonly PreferencesService _preferences;
    private readonly ILogger<TreePageViewModel> _logger;
    private readonly HashSet<string> _collapsed = [];
    private FamilyTreeData _snapshot = new();
    private CancellationTokenSource? _layoutCancellation;
    private bool _initialized;
    private bool _snapshotDirty = true;
    private bool _disposed;
    private bool _settingRoot;
    private string? _pendingFocus;
    private int _selectionVersion;

    public TreePageViewModel(Localizer localizer, DocumentSession document, PreferencesService preferences,
        TreePhotoCache photos, ILogger<TreePageViewModel> logger) : base(localizer, "Nav_Tree", "Tree_Help")
    {
        _localizer = localizer;
        _document = document;
        _preferences = preferences;
        _logger = logger;
        Photos = photos;
        var saved = preferences.Load();
        if (!Enum.IsDefined(saved.Chart) || !Enum.IsDefined(saved.Colors) || !Enum.IsDefined(saved.Orientation)
            || saved.Generations is < 0 or > 10)
        {
            logger.LogWarning("Invalid chart preferences; using default chart settings.");
            saved = new Preferences(saved.Language);
        }
        _chart = saved.Chart;
        _colors = saved.Colors;
        _orientation = saved.Orientation;
        _generations = saved.Generations;
        _showIds = saved.ShowIds;
        _showSex = saved.ShowSex;
        localizer.CultureChanged += OnCultureChanged;
        if (document.Store is { } store) store.Changed += OnDocumentChanged;
    }

    [ObservableProperty] private TreeScene _scene = new([], [], [], 0);
    [ObservableProperty] private IReadOnlyList<Person> _people = [];
    [ObservableProperty] private Person? _selectedPerson;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _focusId;
    [ObservableProperty] private string? _rootId;
    [ObservableProperty] private bool _isOverview;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _relationship = "";
    [ObservableProperty] private TreeChart _chart;
    [ObservableProperty] private TreeColors _colors;
    [ObservableProperty] private TreeOrientation _orientation;
    [ObservableProperty] private int _generations;
    [ObservableProperty] private bool _showIds;
    [ObservableProperty] private bool _showSex;

    public Task LayoutTask { get; private set; } = Task.CompletedTask;
    public Task SelectionTask { get; private set; } = Task.CompletedTask;
    public string DataDirectory => _document.Store?.DataDirectory ?? "";
    public IEnumerable<Person> SearchResults => string.IsNullOrWhiteSpace(SearchText) ? People
        : People.Where(p => p.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
            || p.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    public string SceneStatus => IsBusy ? _localizer["Tree_Loading"]
        : !_document.HasDocument ? _localizer["Tree_DataUnavailable"]
        : People.Count == 0 ? _localizer["Tree_NoPeople"]
        : _localizer.Format("Tree_Visible", Scene.Cards.Count, People.Count);
    public string LayoutWarning => Scene.GenerationConflicts > 0 ? _localizer["Tree_LevelWarning"] : "";
    public IEnumerable<TreeChoice<TreeChart>> ChartChoices => Enum.GetValues<TreeChart>()
        .Select(v => new TreeChoice<TreeChart>(v, _localizer["Tree_Chart_" + v.ToString().ToLowerInvariant()]));
    public IEnumerable<TreeChoice<TreeColors>> ColorChoices => Enum.GetValues<TreeColors>()
        .Select(v => new TreeChoice<TreeColors>(v, _localizer["Tree_Colors_" + v.ToString().ToLowerInvariant()]));
    public IEnumerable<TreeChoice<TreeOrientation>> OrientationChoices => Enum.GetValues<TreeOrientation>()
        .Select(v => new TreeChoice<TreeOrientation>(v, _localizer["Tree_Orientation_" + v.ToString().ToLowerInvariant()]));
    public IEnumerable<TreeChoice<int>> GenerationChoices => Enumerable.Range(0, 11)
        .Select(v => new TreeChoice<int>(v, v == 0 ? _localizer["Tree_Depth_all"] : v.ToString(_localizer.Culture)));

    public TreePhotoCache Photos { get; }
    public event Action? FitRequested;
    public event Action<double>? ZoomRequested;
    public event Action<string>? FocusRequested;

    public Task InitializeAsync()
    {
        if (_initialized) return LayoutTask;
        _initialized = true;
        return LayoutTask = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        _snapshotDirty = true;
        _layoutCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _layoutCancellation = cancellation;
        IsBusy = true;
        ErrorText = _document.LoadError ?? "";
        try
        {
            var snapshot = await Task.Run(() => _document.Store?.GetSnapshot() ?? new FamilyTreeData(), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _snapshot = snapshot;
            _snapshotDirty = false;
            RootId = snapshot.RootPersonId;
            People = snapshot.Persons.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
            OnPropertyChanged(nameof(SearchResults));
            FocusId = snapshot.Persons.Any(p => p.Id == FocusId) ? FocusId
                : snapshot.Persons.FirstOrDefault(p => p.Id == snapshot.RootPersonId)?.Id ?? snapshot.Persons.FirstOrDefault()?.Id;
            SelectedPerson = snapshot.Persons.FirstOrDefault(p => p.Id == (SelectedPerson?.Id ?? (IsOverview ? null : FocusId)));
            await BuildSceneAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ErrorText = _localizer.Format("Tree_Error", ex.Message);
            _logger.LogError(ex, "The desktop tree could not be refreshed.");
        }
        finally
        {
            if (_layoutCancellation == cancellation) { IsBusy = false; _layoutCancellation = null; }
            cancellation.Dispose();
        }
    }

    private async Task RebuildAsync()
    {
        if (!_initialized || _disposed) return;
        if (_snapshotDirty)
        {
            await RefreshAsync();
            return;
        }
        _layoutCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _layoutCancellation = cancellation;
        IsBusy = true;
        try { await BuildSceneAsync(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ErrorText = _localizer.Format("Tree_Error", ex.Message);
            _logger.LogError(ex, "The desktop tree layout failed.");
        }
        finally
        {
            if (_layoutCancellation == cancellation) { IsBusy = false; _layoutCancellation = null; }
            cancellation.Dispose();
        }
    }

    private async Task BuildSceneAsync(CancellationToken token)
    {
        var snapshot = _snapshot;
        var options = new TreeLayoutOptions(FocusId, Chart, Generations, IsOverview, Orientation, _collapsed.ToHashSet());
        var scene = await Task.Run(() => TreeLayout.Build(snapshot, options, token), token);
        token.ThrowIfCancellationRequested();
        Scene = scene;
        OnPropertyChanged(nameof(SceneStatus));
        OnPropertyChanged(nameof(LayoutWarning));
        FitRequested?.Invoke();
        if (_pendingFocus is { } focus)
        {
            _pendingFocus = null;
            FocusRequested?.Invoke(focus);
        }
    }

    public void SelectPerson(string personId, bool focus)
    {
        SelectedPerson = People.FirstOrDefault(p => p.Id == personId);
        if (SelectedPerson is null || !focus) return;
        FocusId = personId;
        IsOverview = false;
        _pendingFocus = personId;
        LayoutTask = RebuildAsync();
    }

    [RelayCommand]
    private void FocusSelected()
    {
        if (SelectedPerson is { } person) SelectPerson(person.Id, true);
    }

    [RelayCommand]
    private void ShowAll()
    {
        IsOverview = true;
        SelectedPerson = null;
        LayoutTask = RebuildAsync();
    }

    [RelayCommand]
    private void ToggleBranch()
    {
        if (SelectedPerson is not { } person || IsOverview) return;
        if (!_collapsed.Add(person.Id)) _collapsed.Remove(person.Id);
        LayoutTask = RebuildAsync();
    }

    [RelayCommand]
    private void ExpandAll()
    {
        _collapsed.Clear();
        LayoutTask = RebuildAsync();
    }

    [RelayCommand]
    private async Task SetRootAsync()
    {
        if (SelectedPerson is not { } person || _document.Store is not { CanEdit: true } store) return;
        _settingRoot = true;
        try
        {
            await Task.Run(() => store.SetRootPerson(person.Id));
            LayoutTask = RefreshAsync();
            await LayoutTask;
            SelectionTask = UpdateDetailsAsync();
            await SelectionTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorText = _localizer.Format("Tree_Error", ex.Message);
            _logger.LogError(ex, "The desktop root person could not be saved.");
        }
        finally { _settingRoot = false; }
    }

    [RelayCommand] private void Fit() => FitRequested?.Invoke();
    [RelayCommand] private void ZoomIn() => ZoomRequested?.Invoke(1.25);
    [RelayCommand] private void ZoomOut() => ZoomRequested?.Invoke(0.8);

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(SearchResults));
    partial void OnSelectedPersonChanged(Person? value) => SelectionTask = UpdateDetailsAsync();
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(SceneStatus));
    partial void OnChartChanged(TreeChart value) { IsOverview = false; SaveSettings(); LayoutTask = RebuildAsync(); }
    partial void OnColorsChanged(TreeColors value) => SaveSettings();
    partial void OnOrientationChanged(TreeOrientation value) { SaveSettings(); LayoutTask = RebuildAsync(); }
    partial void OnGenerationsChanged(int value) { SaveSettings(); LayoutTask = RebuildAsync(); }
    partial void OnShowIdsChanged(bool value) => SaveSettings();
    partial void OnShowSexChanged(bool value) => SaveSettings();

    private void SaveSettings()
    {
        if (!_preferences.TrySave(_preferences.Load() with
            { Chart = Chart, Colors = Colors, Orientation = Orientation, Generations = Generations, ShowIds = ShowIds, ShowSex = ShowSex }))
            ErrorText = _localizer["Tree_SettingsError"];
    }

    private async Task UpdateDetailsAsync()
    {
        var version = ++_selectionVersion;
        Relationship = "";
        if (SelectedPerson is not { } person) { Details = ""; return; }
        var text = new StringBuilder();
        text.AppendLine(person.Id + " | " + _localizer["Sex_" + person.Sex]);
        foreach (var name in person.Names)
            text.AppendLine(_localizer["NameType_" + name.Type] + ": " + name.Full);
        foreach (var ev in person.Events)
            text.AppendLine($"{ev.Type}: {ev.Value} {ev.Date} {ev.Place}".TrimEnd());
        if (!person.Address.IsEmpty) text.AppendLine(person.Address.Summary);
        foreach (var note in person.Notes) text.AppendLine(note);
        foreach (var family in _snapshot.Families.Where(f => f.PartnerIds.Contains(person.Id) || f.ChildRefs.Any(c => c.PersonId == person.Id)))
        {
            text.AppendLine();
            text.AppendLine(family.Id + ": " + _localizer["Fam_" + FamilyStatus.GetStatus(family)]);
            foreach (var member in family.PartnerIds.Concat(family.ChildRefs.Select(c => c.PersonId)).Distinct())
                text.AppendLine("  " + (_snapshot.Persons.FirstOrDefault(p => p.Id == member)?.DisplayName ?? member));
            foreach (var child in family.ChildRefs.Where(c => c.Pedigree != Pedigree.BIRTH))
                text.AppendLine("  " + child.PersonId + ": " + child.Pedigree);
        }
        Details = text.ToString().TrimEnd();
        if (RootId is { } root && _document.Store is { } store)
        {
            var culture = _localizer.Culture;
            var relation = await Task.Run(() => RelationshipLabeler.Label(new RelationshipService(store).Describe(root, person.Id), culture));
            if (!_disposed && version == _selectionVersion)
                Relationship = _localizer.Format("Tree_RelationTo", _snapshot.Persons.FirstOrDefault(p => p.Id == root)?.DisplayName ?? root)
                    + " " + relation;
        }
    }

    private void OnCultureChanged()
    {
        foreach (var name in new[] { nameof(ChartChoices), nameof(ColorChoices), nameof(OrientationChoices), nameof(GenerationChoices),
                     nameof(SceneStatus), nameof(LayoutWarning), nameof(SearchResults) }) OnPropertyChanged(name);
        SelectionTask = UpdateDetailsAsync();
    }

    private void OnDocumentChanged()
    {
        if (!_initialized || _disposed || _settingRoot) return;
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(() => LayoutTask = RefreshAsync());
        else LayoutTask = RefreshAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _layoutCancellation?.Cancel();
        _localizer.CultureChanged -= OnCultureChanged;
        if (_document.Store is { } store) store.Changed -= OnDocumentChanged;
    }
}
