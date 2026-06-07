using System.Windows;
using DebtManager.Desktop.Bootstrap;
using Microsoft.Extensions.DependencyInjection;

namespace DebtManager.Desktop;

public partial class App : System.Windows.Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SQLitePCL.Batteries_V2.Init();
        Services = AppHost.Build();
    }
}
