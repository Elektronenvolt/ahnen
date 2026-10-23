using Ahnen.Web.Components.Pages;
using Ahnen.Web.Models;
using System.Globalization;
using System.Resources;

namespace Ahnen.Tests;

public class NameEditorStateTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EverySupportedTypeHasLocalizedLabelsAndEditorMessages(string culture)
    {
        var resources = new ResourceManager("Ahnen.Web.Resources.SharedResources",
            typeof(Ahnen.Web.SharedResources).Assembly);
        var language = CultureInfo.GetCultureInfo(culture);
        foreach (var type in Enum.GetValues<NameType>())
        {
            Assert.False(string.IsNullOrEmpty(resources.GetString($"Name_Given_{type}", language)));
            Assert.False(string.IsNullOrEmpty(resources.GetString($"Name_Surname_{type}", language)));
        }
        foreach (var key in new[]
                 {
                     "Name_Recorded", "Name_Entry", "Name_EntryLabel", "Name_EmptyEntry", "Name_NewEntry",
                     "Name_NoRecord", "Name_TypeHelp", "Name_MoreDetails", "Name_AddAnother", "Name_Remove",
                     "Name_KeepOne", "Name_Retry", "Name_Reload", "Name_PersonMissing", "Name_Conflict",
                     "Name_SaveError", "Name_UndoError", "Name_UndoUnavailable"
                 })
            Assert.False(string.IsNullOrEmpty(resources.GetString(key, language)), key);
    }

    [Fact]
    public void SelectingTypesKeepsSeparateNamesAndOriginalOrder()
    {
        var state = Create(
            new() { Given = "Erika", Surname = "Schmidt" },
            new() { Type = NameType.MARRIED, Given = "Erika", Surname = "Mustermann" });

        state.SelectType(NameType.MARRIED);
        Assert.Equal("Mustermann", state.Current.Surname);
        state.SelectType(NameType.BIRTH);
        Assert.Equal("Schmidt", state.Current.Surname);
        Assert.False(state.HasChanges);
        Assert.Equal([NameType.BIRTH, NameType.MARRIED], state.PendingNames().Select(n => n.Type));
    }

    [Theory]
    [InlineData(NameType.BIRTH)]
    [InlineData(NameType.MARRIED)]
    [InlineData(NameType.AKA)]
    [InlineData(NameType.MAIDEN)]
    [InlineData(NameType.IMMIGRANT)]
    [InlineData(NameType.PROFESSIONAL)]
    [InlineData(NameType.OTHER)]
    public void MissingTypeStaysDraftUntilContentIsEntered(NameType type)
    {
        var state = Create();
        state.SelectType(type);
        Assert.True(state.IsDraft);
        Assert.False(state.HasChanges);
        Assert.Empty(state.PendingNames());

        state.Current.Given = "  Érika  ";
        Assert.True(state.HasChanges);
        var pending = Assert.Single(state.PendingNames());
        Assert.Equal(type, pending.Type);
        Assert.Equal("  Érika  ", pending.Given);
        state.AcceptSave();
        Assert.False(state.IsDraft);
        Assert.False(state.HasChanges);
    }

    [Fact]
    public void DuplicateEntriesRemainIndependentAndNewEntryIsAppended()
    {
        var state = Create(
            new() { Type = NameType.AKA, Given = "First" },
            new() { Type = NameType.BIRTH, Given = "Primary birth" },
            new() { Type = NameType.AKA, Given = "Second" });
        Assert.Equal([0, 2], state.Entries);
        state.SelectEntry(2);
        state.Current.Given = "Changed";
        state.AcceptSave();
        state.AddAnother();
        Assert.False(state.HasChanges);
        state.Current.Suffix = " Jr. ";
        state.AcceptSave();

        Assert.Equal(["First", "Primary birth", "Changed", ""], state.PendingNames().Select(n => n.Given));
        Assert.Equal(" Jr. ", state.Current.Suffix);
        Assert.Equal(3, state.SelectedIndex);
        Assert.Equal(3, state.Count(NameType.AKA));
    }

    [Fact]
    public void RemovingSelectedDuplicateKeepsOtherRecordsAndSelectsRemainingEntry()
    {
        var state = Create(new() { Given = "First" }, new() { Given = "Second" });
        state.SelectEntry(1);
        state.RemoveSelected();
        Assert.True(state.HasChanges);
        Assert.Equal("First", state.Current.Given);
        Assert.False(state.CanRemove);
        state.RemoveSelected();
        Assert.Single(state.PendingNames());
    }

    [Fact]
    public void DraftCannotRemoveExistingRecord()
    {
        var state = Create(new() { Given = "First" }, new() { Given = "Second" });
        state.SelectType(NameType.MARRIED);
        state.RemoveSelected();
        Assert.False(state.CanRemove);
        Assert.Equal(2, state.PendingNames().Count);
        Assert.False(state.HasChanges);
    }

    [Fact]
    public void ReloadDiscardsPendingEditsAndCanPreserveTypeSelection()
    {
        var original = new PersonName { Given = "Birth" };
        var state = Create(original, new() { Type = NameType.MARRIED, Given = "Married" });
        state.SelectType(NameType.MARRIED);
        state.Current.Given = "Pending";
        state.Load([original], preserveSelection: true);
        Assert.Equal(NameType.MARRIED, state.SelectedType);
        Assert.True(state.IsDraft);
        Assert.False(state.HasChanges);
        state.Load([original]);
        Assert.Equal(NameType.BIRTH, state.SelectedType);
        Assert.Equal("Birth", state.Current.Given);
    }

    [Fact]
    public void LoadedAndPendingNamesAreCopiesAndOptionalValuesAreVerbatim()
    {
        var original = new PersonName
        {
            Given = "  Érika ", Surname = "von  Schmidt", Prefix = " Dr. ",
            Suffix = " III ", Nickname = " Riki "
        };
        var state = Create(original);
        state.Current.Surname = " New ";
        var pending = Assert.Single(state.PendingNames());
        Assert.Equal("von  Schmidt", original.Surname);
        Assert.Equal(" Dr. ", pending.Prefix);
        Assert.Equal(" III ", pending.Suffix);
        Assert.Equal(" Riki ", pending.Nickname);
        Assert.Equal("  Érika ", pending.Given);
        pending.Prefix = "Outside edit";
        Assert.Equal(" Dr. ", state.Current.Prefix);
    }

    [Fact]
    public void StaleSelectionIsDetectedWithoutMistakingLocalChangesForExternalEdits()
    {
        var original = new PersonName { Given = "First" };
        var state = Create(original, new() { Given = "Second" });
        state.Current.Given = "Local change";
        Assert.True(state.MatchesBaseline([original, new() { Given = "Second" }]));
        Assert.False(state.MatchesBaseline([new() { Given = "External" }, new() { Given = "Second" }]));
        Assert.False(state.MatchesBaseline([new() { Given = "Second" }, original]));
        Assert.False(state.MatchesBaseline([original]));
        Assert.Throws<InvalidOperationException>(() => state.SelectType(NameType.MARRIED));
        Assert.Throws<InvalidOperationException>(() => state.AddAnother());
    }

    [Fact]
    public void ClearingDraftBeforeSaveDoesNotCreateEmptyRecord()
    {
        var state = Create(new PersonName { Given = "First" });
        state.AddAnother();
        state.Current.Given = "Draft";
        state.Current.Given = "";
        Assert.False(state.HasChanges);
        Assert.Single(state.PendingNames());
    }

    [Fact]
    public void ExistingRecordCanBeClearedAndWhitespaceOnlyDraftIsPreserved()
    {
        var state = Create(new PersonName { Given = "First" });
        state.Current.Given = "";
        Assert.True(state.HasChanges);
        state.AcceptSave();
        Assert.Equal("", Assert.Single(state.PendingNames()).Given);
        state.AddAnother();
        state.Current.Prefix = " ";
        Assert.True(state.HasChanges);
        Assert.Equal(" ", state.PendingNames()[1].Prefix);
    }

    private static NameEditorState Create(params PersonName[] names)
    {
        var state = new NameEditorState();
        state.Load(names);
        return state;
    }
}