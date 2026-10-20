// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================
//
//  Author : Claudio Andino
//  Email  : claudio@patagoniarobot.com
//
//  Copyright (c) 2026 Claudio Andino
//  Developed under the Patagonia Robot initiative
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at:
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
// =============================================================================

using System.Windows.Input;
using ForZip.Core.Interfaces;

namespace ForZip.GUI.ViewModels;

/// <summary>
/// Pantalla de inicio: las tres tareas, grandes, antes que cualquier formulario. Quien abre ForZip por primera vez
/// ve qué puede hacer en vez de un formulario de empaquetado con opciones que todavía no entiende. Los comandos son
/// los mismos de la barra lateral.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    public HomeViewModel(ILocalizationService localization, ICommand pack, ICommand extract, ICommand verify)
        : base(localization)
    {
        PackCommand = pack;
        ExtractCommand = extract;
        VerifyCommand = verify;
    }

    public ICommand PackCommand { get; }
    public ICommand ExtractCommand { get; }
    public ICommand VerifyCommand { get; }

    public string Title => _localization.Get("home_title");
    public string Subtitle => _localization.Get("home_subtitle");
    public string PackTitle => _localization.Get("home_pack_title");
    public string PackText => _localization.Get("home_pack_text");
    public string PackAction => _localization.Get("home_pack_action");
    public string ExtractTitle => _localization.Get("home_extract_title");
    public string ExtractText => _localization.Get("home_extract_text");
    public string ExtractAction => _localization.Get("home_extract_action");
    public string VerifyTitle => _localization.Get("home_verify_title");
    public string VerifyText => _localization.Get("home_verify_text");
    public string VerifyAction => _localization.Get("home_verify_action");
    public string Tip => _localization.Get("home_tip");

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(string.Empty);
    }
}
