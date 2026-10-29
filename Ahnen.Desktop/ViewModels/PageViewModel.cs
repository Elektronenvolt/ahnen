using Ahnen.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ahnen.Desktop.ViewModels;

/// <summary>A navigable page with a localized title and description that follow language changes.</summary>
public abstract class PageViewModel : ObservableObject
{
    private readonly Localizer _localizer;
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    protected PageViewModel(Localizer localizer, string titleKey, string descriptionKey)
    {
        _localizer = localizer;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        localizer.CultureChanged += () =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
        };
    }

    public string Title => _localizer[_titleKey];
    public string Description => _localizer[_descriptionKey];
}

public sealed class EditPageViewModel(Localizer localizer)
    : PageViewModel(localizer, "Nav_Edit", "Page_Edit_Placeholder");
