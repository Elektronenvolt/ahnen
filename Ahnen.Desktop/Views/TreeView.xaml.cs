using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ahnen.Core.Models;
using Ahnen.Desktop.Localization;
using Ahnen.Desktop.ViewModels;

namespace Ahnen.Desktop.Views;

public partial class TreeView : UserControl
{
    private TreePageViewModel? _model;

    public TreeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_model is not null || DataContext is not TreePageViewModel model) return;
        _model = model;
        model.FitRequested += Canvas.Fit;
        model.ZoomRequested += Canvas.Zoom;
        model.FocusRequested += Canvas.CenterOn;
        Canvas.PersonActivated += OnPersonActivated;
        Localizer.Instance.CultureChanged += Canvas.InvalidateVisual;
        await model.InitializeAsync();
        Canvas.Fit();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_model is not { } model) return;
        model.FitRequested -= Canvas.Fit;
        model.ZoomRequested -= Canvas.Zoom;
        model.FocusRequested -= Canvas.CenterOn;
        Canvas.PersonActivated -= OnPersonActivated;
        Localizer.Instance.CultureChanged -= Canvas.InvalidateVisual;
        _model = null;
    }

    private void OnPersonActivated(string personId, bool focus) => _model?.SelectPerson(personId, focus);

    private void People_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(PeopleList, source) is ListBoxItem
            && PeopleList.SelectedItem is Person person)
            _model?.SelectPerson(person.Id, (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
    }

    private void People_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || PeopleList.SelectedItem is not Person person) return;
        _model?.SelectPerson(person.Id, (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
        e.Handled = true;
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Down && _model?.SearchResults.FirstOrDefault() is { } person)
        {
            PeopleList.SelectedItem = person;
            if (e.Key == Key.Enter) _model.SelectPerson(person.Id, true);
            else PeopleList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Search.Clear();
            Canvas.Focus();
            e.Handled = true;
        }
    }
}
