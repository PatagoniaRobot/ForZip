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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ForZip.Core.Interfaces;
using ForZip.Core.Services;
using ForZip.GUI.Services;
using ForZip.GUI.ViewModels;
using ForZip.GUI.Views;

namespace ForZip.GUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: instanciamos servicios con sus dependencias resueltas a mano
            var localization = new LocalizationService();
            var logService = new LogService();
            var hashService = new HashService();
            var passwordService = new PasswordService();
            var zipService = new ZipService(hashService);
            var reportService = new ReportService(localization);
            var configService = new ConfigService();
            var themeService = new ThemeService();
            var operatorDialog = new OperatorDialogService();

            var mainViewModel = new MainWindowViewModel(
                zipService, hashService, passwordService,
                reportService, configService, localization, themeService,
                logService, operatorDialog);

            UpdateResources(localization);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void UpdateResources(ILocalizationService localization)
    {
        foreach (var key in localization.GetAllKeys())
        {
            Resources[key] = localization.Get(key);
        }
    }
}
