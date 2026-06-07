using DebtManager.Desktop.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace DebtManager.Desktop.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
    }

    private void DialogOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent click-through to elements behind
        e.Handled = true;
    }

    private void CloseDialogOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        // Close dialog when clicking the overlay background
        if (DataContext is ShellViewModel vm)
        {
            vm.CloseDialogCommand.Execute(null);
        }
    }
}
