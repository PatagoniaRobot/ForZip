using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ForZip.Core.Models;
using ForZip.GUI.ViewModels;

namespace ForZip.GUI.Views;

public partial class OperatorConfirmationView : Window
{
    public OperatorConfirmationView()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private async void OnBrowseCertClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OperatorConfirmationViewModel vm)
        {
            return;
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Seleccionar certificado del operador",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Certificado PKCS#12")
                {
                    Patterns = new[] { "*.pfx", "*.p12" }
                }
            }
        });

        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.CertificatePath = path;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorConfirmationViewModel vm)
        {
            // Con un certificado que no abre, el diálogo no se cierra: se corrige acá y no al final del trabajo.
            var prefix = this.TryFindResource("operator_cert_unusable", out var text) && text is string s
                ? s
                : "El certificado no sirve para firmar:";
            if (!vm.TryValidateCertificate(prefix))
            {
                return;
            }

            Close(vm.GetResult());
        }
    }
}
