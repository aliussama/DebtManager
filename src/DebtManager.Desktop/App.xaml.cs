using System.Windows;
using System.Windows.Threading;
using DebtManager.Desktop.Bootstrap;
using DebtManager.Desktop.Services;
using DebtManager.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DebtManager.Desktop;

public partial class App : System.Windows.Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize diagnostics first
        var startupCid = AppDiagnostics.NewCorrelationId();
        AppDiagnostics.WriteInfo("Startup", "Application starting", startupCid);
        AppDiagnostics.PruneOldLogs();

        // Write crash marker (cleared on clean exit)
        AppDiagnostics.WriteCrashMarker(startupCid, "Startup", "None");

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var cid = AppDiagnostics.GetCurrentCorrelationId();
            if (args.ExceptionObject is Exception ex)
                AppDiagnostics.WriteError("UnhandledException", ex, cid);
            AppDiagnostics.WriteCrashMarker(cid, "UnhandledException", "Unknown");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            var cid = AppDiagnostics.GetCurrentCorrelationId();
            AppDiagnostics.WriteError("DispatcherException", args.Exception, cid);
            args.Handled = true; // prevent crash, allow graceful recovery
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            var cid = AppDiagnostics.GetCurrentCorrelationId();
            AppDiagnostics.WriteError("UnobservedTask", args.Exception, cid);
            args.SetObserved();
        };

        SQLitePCL.Batteries_V2.Init();
        Services = AppHost.Build();

        // Apply persisted theme before MainWindow renders
        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(themeService.CurrentTheme);

        // Expose IFocusRequestService as an application-level resource
        // so XAML views can bind to it via {StaticResource FocusService}
        Resources["FocusService"] = Services.GetRequiredService<IFocusRequestService>();

        AppDiagnostics.WriteInfo("Startup", "Application started successfully", startupCid);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppDiagnostics.ClearCrashMarker();
        AppDiagnostics.WriteInfo("Shutdown", "Application exiting cleanly");
        base.OnExit(e);
    }
}