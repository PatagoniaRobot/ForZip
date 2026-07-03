using CommunityToolkit.Mvvm.ComponentModel;
using ForZip.Core.Interfaces;

namespace ForZip.GUI.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected readonly ILocalizationService _localization;

    protected ViewModelBase(ILocalizationService localization)
    {
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    protected virtual void OnLanguageChanged()
    {
        // Sobrescribir en ViewModels para refrescar propiedades localizadas
    }
}
