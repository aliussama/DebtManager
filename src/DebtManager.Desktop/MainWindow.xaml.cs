using DebtManager.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace DebtManager.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shellViewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Get ShellViewModel from DI
        _shellViewModel = App.Services.GetRequiredService<ShellViewModel>();
        
        // Set DataContext for the ShellView
        ShellViewControl.DataContext = _shellViewModel;

        // Initialize on load
        Loaded += async (s, e) =>
        {
            await _shellViewModel.InitializeAsync();
        };
    }
}
