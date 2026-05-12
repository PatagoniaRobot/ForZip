using Avalonia.Controls;
using Avalonia.Interactivity;
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

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorConfirmationViewModel vm)
        {
            Close(vm.GetResult());
        }
    }
}
