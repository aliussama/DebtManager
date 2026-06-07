using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using DebtManager.Infrastructure.Rules;
using DebtManager.Domain.Rules;
using DebtManager.Application.Simulation;
using DebtManager.Infrastructure.Security;
using DebtManager.Desktop.Security;

namespace DebtManager.Desktop.Bootstrap;

public static class AppHost
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // Local DB path (AppData)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = Path.Combine(appData, "DebtManager");
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "debtmanager_local.db");

        // Infrastructure
        services.AddSingleton<DebtManager.Domain.Events.IEventStore, SqliteEventStore>();
        services.AddSingleton<IKeyStore, DpapiKeyStore>();

        services.AddSingleton(sp =>
        {
            var keys = sp.GetRequiredService<IKeyStore>();
            return new DebtManager.Infrastructure.Persistence.SqliteConnectionFactory(dbPath, keys);
        });

        // Application handlers
        services.AddTransient<CreateObligationHandler>();
        services.AddTransient<DefineScheduleHandler>();
        services.AddTransient<RecordPaymentHandler>();
        services.AddTransient<GetFinancialSnapshotHandler>();

        services.AddSingleton<IRulePackRepository, SqliteRulePackRepository>();
        services.AddSingleton<IRulePackResolver, SqliteRulePackResolver>();
        services.AddSingleton<IRuleEngine, SqliteRuleEngine>();

        services.AddTransient<InstallRulePackHandler>();
        services.AddTransient<AssignRulePackToObligationHandler>();

        services.AddTransient<SimulateScenarioHandler>();

        services.AddSingleton<DeviceIdentityProvider>();

        return services.BuildServiceProvider();
    }
}
