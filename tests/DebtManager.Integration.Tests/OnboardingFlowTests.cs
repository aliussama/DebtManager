using DebtManager.Application.UseCases;
using DebtManager.Desktop.ViewModels;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using DebtManager.Infrastructure.Security;

namespace DebtManager.Integration.Tests;

public sealed class OnboardingFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _configPath;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteConnectionFactory _factory;

    public OnboardingFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"OnboardingFlowTests_{Guid.NewGuid()}.db");
        _configPath = Path.Combine(Path.GetTempPath(), $"OnboardingFlowTests_config_{Guid.NewGuid()}.json");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
    }

    public void Dispose()
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                if (File.Exists(_configPath)) File.Delete(_configPath);
                break;
            }
            catch (IOException) when (i < 29)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task FirstRun_OnboardingVisible_WhenFlagMissing()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var shell = CreateShellViewModel(config);

        // HasCompletedOnboarding is not set — InitializeAsync should show onboarding
        await shell.InitializeAsync();

        Assert.True(shell.IsOnboardingVisible);
        Assert.NotNull(shell.OnboardingVm);
    }

    [Fact]
    public async Task FirstRun_OnboardingHidden_WhenFlagTrue()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        config.Set(ConfigKeys.HasCompletedOnboarding, bool.TrueString);

        var shell = CreateShellViewModel(config);
        await shell.InitializeAsync();

        Assert.False(shell.IsOnboardingVisible);
        Assert.Null(shell.OnboardingVm);
    }

    [Fact]
    public void CompletingOnboarding_SetsFlag_AndHidesWizard()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var shell = CreateShellViewModel(config);

        // Show onboarding
        shell.StartOnboarding();
        Assert.True(shell.IsOnboardingVisible);

        // Fill valid values on the OnboardingViewModel
        var vm = shell.OnboardingVm!;
        vm.DefaultCurrencyCode = "USD";
        vm.DefaultTimeZone = "America/New_York";

        // Navigate to last step and finish
        vm.NextCommand.Execute(null); // step 0 -> 1
        vm.NextCommand.Execute(null); // step 1 -> 2
        vm.FinishCommand.Execute(null);

        // Assert completion
        Assert.False(shell.IsOnboardingVisible);
        Assert.Null(shell.OnboardingVm);
        Assert.Equal(bool.TrueString, config.Get(ConfigKeys.HasCompletedOnboarding));
        Assert.Equal("USD", config.Get(ConfigKeys.DefaultCurrency));
        Assert.Equal("America/New_York", config.Get(ConfigKeys.DefaultTimeZone));
    }

    [Fact]
    public void SkipOnboarding_SavesMinimalDefaults()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var shell = CreateShellViewModel(config);

        shell.StartOnboarding();
        var vm = shell.OnboardingVm!;

        // Keep defaults, just skip
        vm.SkipCommand.Execute(null);

        // Assert flag set and defaults saved
        Assert.False(shell.IsOnboardingVisible);
        Assert.Equal(bool.TrueString, config.Get(ConfigKeys.HasCompletedOnboarding));

        var savedCurrency = config.Get(ConfigKeys.DefaultCurrency);
        var savedTimezone = config.Get(ConfigKeys.DefaultTimeZone);
        Assert.False(string.IsNullOrEmpty(savedCurrency));
        Assert.False(string.IsNullOrEmpty(savedTimezone));
    }

    [Fact]
    public void RerunOnboarding_FromSettings_ShowsWizard()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        config.Set(ConfigKeys.HasCompletedOnboarding, bool.TrueString);

        var shell = CreateShellViewModel(config);

        // Onboarding is not visible since completed
        Assert.False(shell.IsOnboardingVisible);

        // Simulate clicking "Run onboarding again" from Settings
        shell.StartOnboarding();

        Assert.True(shell.IsOnboardingVisible);
        Assert.NotNull(shell.OnboardingVm);
    }

    [Fact]
    public void OnboardingViewModel_Validation_Currency_RejectsInvalid()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var completed = false;
        var vm = new OnboardingViewModel(config, Guid.NewGuid(), () => completed = true);

        vm.DefaultCurrencyCode = "xx";
        Assert.NotNull(vm.CurrencyError);

        vm.DefaultCurrencyCode = "USD";
        Assert.Null(vm.CurrencyError);
    }

    [Fact]
    public void OnboardingViewModel_Validation_SyncBaseUrl_RequiresApiKey()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var vm = new OnboardingViewModel(config, Guid.NewGuid(), () => { });

        vm.SyncBaseUrl = "https://sync.example.com";
        // API key should be required
        Assert.NotNull(vm.SyncApiKeyError);

        vm.SyncApiKey = "my-key";
        Assert.Null(vm.SyncApiKeyError);
    }

    [Fact]
    public void OnboardingViewModel_Validation_VaultId_MustBeGuid()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var vm = new OnboardingViewModel(config, Guid.NewGuid(), () => { });

        vm.SyncBaseUrl = "https://sync.example.com";
        vm.SyncApiKey = "key";
        vm.SyncVaultId = "not-a-guid";
        Assert.NotNull(vm.SyncVaultIdError);

        vm.SyncVaultId = Guid.NewGuid().ToString();
        Assert.Null(vm.SyncVaultIdError);
    }

    [Fact]
    public void OnboardingViewModel_StepNavigation_WorksCorrectly()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var vm = new OnboardingViewModel(config, Guid.NewGuid(), () => { });

        // Start at step 0
        Assert.Equal(0, vm.CurrentStepIndex);
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsLastStep);

        // Can't go back from step 0
        Assert.False(vm.CanBack);

        // Go to step 1
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStepIndex);
        Assert.True(vm.CanBack);

        // Go to step 2
        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStepIndex);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.CanNext);
        Assert.True(vm.CanFinish);

        // Go back
        vm.BackCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStepIndex);
    }

    [Fact]
    public void OnboardingViewModel_Finish_SavesSyncConfig()
    {
        var config = new SecureConfiguration(new TestKeyStore(), _configPath);
        var completed = false;
        var vm = new OnboardingViewModel(config, Guid.NewGuid(), () => completed = true);

        vm.DefaultCurrencyCode = "EUR";
        vm.DefaultTimeZone = "Europe/Paris";
        vm.SyncBaseUrl = "https://sync.example.com";
        vm.SyncApiKey = "test-key";
        vm.SyncVaultId = Guid.NewGuid().ToString();

        // Navigate to last step
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);
        vm.FinishCommand.Execute(null);

        Assert.True(completed);
        Assert.Equal("EUR", config.Get(ConfigKeys.DefaultCurrency));
        Assert.Equal("https://sync.example.com", config.Get(ConfigKeys.SyncBaseUrl));
        Assert.Equal("test-key", config.Get(ConfigKeys.SyncApiKey));
    }

    private ShellViewModel CreateShellViewModel(SecureConfiguration config)
    {
        var rulePackRepo = new SqliteRulePackRepository(_factory);
        var resolver = new SqliteRulePackResolver(_eventStore);
        var ruleEngine = new SqliteRuleEngine(rulePackRepo, resolver);

        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var dashboardVm = new DashboardViewModel(dashboardHandler);

        return new ShellViewModel(
            _eventStore,
            ruleEngine,
            dashboardVm,
            actorUserId: Guid.NewGuid(),
            deviceId: Guid.NewGuid(),
            secureConfiguration: config);
    }
}
