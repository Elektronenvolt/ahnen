using System.Reflection;
using Ahnen.Core.Models;
using Ahnen.Core.Services;
using Ahnen.Web.Components.Pages;
using Ahnen.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ahnen.Tests;

public class EventNotesUiTests
{
    [Fact]
    public async Task Editor_saves_notes_only_events_and_distinct_marriages_and_detail_panel_shows_them()
    {
        using var testStore = new TestStore();
        var store = testStore.Store;
        var person = store.AddPerson("Alex", "Example");
        var partner1 = store.AddPerson("Pat", "First");
        var partner2 = store.AddPerson("Sam", "Second");
        var first = store.AddPartner(person.Id, partner1.Id);
        var second = store.AddPartner(person.Id, partner2.Id);
        FamilyStatus.ApplyStatus(first, "MARR");
        FamilyStatus.ApplyStatus(second, "MARR");
        store.UpdateFamily(first);
        store.UpdateFamily(second);
        person = store.GetPerson(person.Id)!;
        person.GetOrAddEvent(IndividualEventType.OCCU).Notes = ["Existing occupation note"];
        store.UpdatePersonFacts(person);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton(store);
        services.AddSingleton<PhotoService>();
        services.AddSingleton<RelationshipService>();
        services.AddSingleton<ChartDataService>();
        services.AddSingleton<IJSRuntime, NoJsRuntime>();
        services.AddSingleton<CapturingActivator>();
        services.AddSingleton<IComponentActivator>(sp => sp.GetRequiredService<CapturingActivator>());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var activator = provider.GetRequiredService<CapturingActivator>();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            activator.PersonQuery = person.Id;
            var editor = await renderer.RenderComponentAsync<Edit>(ParameterView.Empty);
            var edit = activator.Get<Edit>();
            var html = editor.ToHtmlString();
            Assert.Contains("Common notes", html);
            Assert.Contains("Birth notes", html);
            Assert.Contains("Death notes", html);
            Assert.Contains("Cause of death", html);
            Assert.Equal(2, html.Split("Marriage notes").Length - 1);
            Assert.Equal(4, html.Split("class=\"input event-notes-input\" rows=\"1\"").Length - 1);
            Assert.Contains("class=\"input postal-code-input\" maxlength=\"5\" inputmode=\"numeric\"", html);

            SetField(edit, "_notes", " Common ");
            SetField(edit, "_birthNotes", " Birth one \n\nBirth two ");
            SetField(edit, "_deathCause", "Natural causes");
            SetField(edit, "_deathNotes", "<death>");
            Invoke(edit, "SaveNow");
            Invoke(edit, "SetMarriageNotes", first.Id, " First note \n First second line ");
            Invoke(edit, "SetMarriageNotes", second.Id, "Second note");

            var saved = store.GetPerson(person.Id)!;
            Assert.Equal(["Common"], saved.Notes);
            Assert.Equal(["Birth one", "Birth two"], saved.Birth!.Notes);
            Assert.Equal(["<death>"], saved.Death!.Notes);
            Assert.Equal("Natural causes", saved.Death.Cause);
            Assert.True(saved.IsDeceased);
            Assert.Equal(["Existing occupation note"], saved.Events.Single(e => e.Type == IndividualEventType.OCCU).Notes);
            Assert.Equal(["First note", "First second line"], store.GetFamily(first.Id)!.Marriage!.Notes);
            Assert.Equal(["Second note"], store.GetFamily(second.Id)!.Marriage!.Notes);

            var tree = await renderer.RenderComponentAsync<Tree>(ParameterView.Empty);
            var detail = tree.ToHtmlString();
            Assert.Contains("Birth notes", detail);
            Assert.Contains("Birth one", detail);
            Assert.Contains("Death notes", detail);
            Assert.Contains("&lt;death&gt;", detail);
            Assert.Contains("Cause of death", detail);
            Assert.Contains("Natural causes", detail);
            Assert.Contains("Common notes", detail);
            Assert.Contains("Marriage notes with Pat First", detail);
            Assert.Contains("Marriage notes with Sam Second", detail);
            Assert.Contains("First second line", detail);
            Assert.Contains("Second note", detail);

            Invoke(edit, "Select", partner1.Id);
            Invoke(edit, "StateHasChanged");
            html = editor.ToHtmlString();
            Assert.Contains("rows=\"1\">First note", html);
            Assert.DoesNotContain("Second note", html);
            activator.PersonQuery = partner1.Id;
            var partnerTree = await renderer.RenderComponentAsync<Tree>(ParameterView.Empty);
            Assert.Contains("Marriage notes with Alex Example", partnerTree.ToHtmlString());
            Assert.Contains("First note", partnerTree.ToHtmlString());

            Invoke(edit, "Select", person.Id);
            Invoke(edit, "StateHasChanged");
            Assert.Contains("rows=\"1\">Birth one", editor.ToHtmlString());
            Assert.Equal("Natural causes", GetField(edit, "_deathCause"));
            SetField(edit, "_birthDate", "not a date");
            SetField(edit, "_birthNotes", "Not saved yet");
            Invoke(edit, "SaveNow");
            Assert.Equal(["Birth one", "Birth two"], store.GetPerson(person.Id)!.Birth!.Notes);

            SetField(edit, "_birthDate", "1950");
            SetField(edit, "_birthNotes", "");
            SetField(edit, "_deathNotes", "");
            Invoke(edit, "SaveNow");
            Assert.Equal("1950", store.GetPerson(person.Id)!.Birth!.Date);
            Assert.Empty(store.GetPerson(person.Id)!.Birth!.Notes);
            Assert.Equal("Natural causes", store.GetPerson(person.Id)!.Death!.Cause);
            SetField(edit, "_deathCause", "");
            Invoke(edit, "SaveNow");
            Assert.Null(store.GetPerson(person.Id)!.Death);
            Assert.Equal(["Common"], store.GetPerson(person.Id)!.Notes);
            SetField(edit, "_birthDate", "");
            Invoke(edit, "SaveNow");
            Assert.Null(store.GetPerson(person.Id)!.Birth);
            Invoke(edit, "SetMarriageNotes", first.Id, "");
            Assert.Empty(store.GetFamily(first.Id)!.Marriage!.Notes);
            Assert.Equal(["Second note"], store.GetFamily(second.Id)!.Marriage!.Notes);
            Invoke(edit, "SetStatus", second.Id, "DIV");
            Assert.Equal(["Second note"], store.GetFamily(second.Id)!.Marriage!.Notes);
            Invoke(edit, "SetStatus", second.Id, "ENGA");
            Assert.Null(store.GetFamily(second.Id)!.Marriage);
        });
    }

    private static void SetField(object component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static object? GetField(object component, string name) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component);

    private static void Invoke(object component, string name, params object?[] arguments) =>
        component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);

    private sealed class CapturingActivator(IServiceProvider services) : IComponentActivator
    {
        private readonly Dictionary<Type, IComponent> _instances = [];
        public string? PersonQuery { get; set; }

        public IComponent CreateInstance(Type componentType)
        {
            var instance = (IComponent)ActivatorUtilities.CreateInstance(services, componentType);
            if (instance is Edit edit) edit.PersonQuery = PersonQuery;
            if (instance is Tree tree) tree.PersonQuery = PersonQuery;
            _instances[componentType] = instance;
            return instance;
        }

        public T Get<T>() where T : IComponent => (T)_instances[typeof(T)];
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
