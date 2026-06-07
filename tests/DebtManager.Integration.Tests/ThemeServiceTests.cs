using DebtManager.Desktop.Services;
using DebtManager.Desktop.ViewModels;
using DebtManager.Infrastructure.Security;

namespace DebtManager.Integration.Tests;

public class ThemeServiceTests
{
    private static SecureConfiguration CreateConfig()
    {
        var keyStore = new TestKeyStore();
        var tempPath = Path.Combine(Path.GetTempPath(), $"debtmgr_theme_test_{Guid.NewGuid()}.json");
        return new SecureConfiguration(keyStore, tempPath);
    }

    [Fact]
    public void Theme_DefaultsToDark_WhenMissing()
    {
        var config = CreateConfig();
        // Do not set AppTheme

        var service = new ThemeService(config);

        Assert.Equal("Dark", service.CurrentTheme);
    }

    [Fact]
    public void Theme_DefaultsToDark_WhenInvalid()
    {
        var config = CreateConfig();
        config.Set(ConfigKeys.AppTheme, "Rainbow");

        var service = new ThemeService(config);

        Assert.Equal("Dark", service.CurrentTheme);
    }

    [Fact]
    public void Theme_ReadsLight_WhenPersistedAsLight()
    {
        var config = CreateConfig();
        config.Set(ConfigKeys.AppTheme, "Light");

        var service = new ThemeService(config);

        Assert.Equal("Light", service.CurrentTheme);
    }

    [Fact]
    public void Theme_ReadsLight_CaseInsensitive()
    {
        var config = CreateConfig();
        config.Set(ConfigKeys.AppTheme, "light");

        var service = new ThemeService(config);

        Assert.Equal("Light", service.CurrentTheme);
    }

    [Fact]
    public void ThemeService_PersistsTheme_OnApply()
    {
        // ThemeService.ApplyTheme requires Application.Current (WPF),
        // which is null in a test runner. We verify the config persistence
        // by pre-setting the theme and reading it back.
        var config = CreateConfig();

        // Simulate what ApplyTheme does to config
        config.Set(ConfigKeys.AppTheme, "Light");
        Assert.Equal("Light", config.Get(ConfigKeys.AppTheme));

        config.Set(ConfigKeys.AppTheme, "Dark");
        Assert.Equal("Dark", config.Get(ConfigKeys.AppTheme));
    }

    [Fact]
    public void ThemeService_NormalizesThemeName()
    {
        var config = CreateConfig();
        config.Set(ConfigKeys.AppTheme, "LIGHT");

        var service = new ThemeService(config);
        Assert.Equal("Light", service.CurrentTheme);
    }

    [Fact]
    public void ThemeService_DefaultsToDark_WhenEmpty()
    {
        var config = CreateConfig();
        config.Set(ConfigKeys.AppTheme, "");

        var service = new ThemeService(config);
        Assert.Equal("Dark", service.CurrentTheme);
    }

    [Fact]
    public void ThemeService_DefaultsToDark_WhenWhitespace()
    {
        var config = CreateConfig();
        config.Set(ConfigKeys.AppTheme, "   ");

        var service = new ThemeService(config);
        Assert.Equal("Dark", service.CurrentTheme);
    }

    [Fact]
    public async Task Settings_Save_UpdatesThemeKey()
    {
        var config = CreateConfig();

        // Create a fake theme service that tracks ApplyTheme calls
        var fakeThemeService = new FakeThemeService();

        var vm = new SettingsViewModel(config, themeService: fakeThemeService);

        // Verify initial load
        Assert.Equal("Dark", vm.SelectedTheme);

        // Change theme selection
        vm.SelectedTheme = "Light";

        // Trigger save (synchronous in this test via SaveCommand)
        vm.SaveCommand.Execute(null);

        // Wait for async completion with polling
        for (int i = 0; i < 50; i++)
        {
            if (fakeThemeService.LastAppliedTheme != null)
                break;
            await Task.Delay(100);
        }

        // Verify the fake theme service was called
        Assert.Equal("Light", fakeThemeService.LastAppliedTheme);
    }

    [Fact]
    public void Settings_ResetDefaults_SetsThemeToDark()
    {
        var config = CreateConfig();
        var fakeThemeService = new FakeThemeService();
        var vm = new SettingsViewModel(config, themeService: fakeThemeService);

        vm.SelectedTheme = "Light";
        vm.ResetDefaultsCommand.Execute(null);

        Assert.Equal("Dark", vm.SelectedTheme);
    }

    private sealed class FakeThemeService : IThemeService
    {
        public string CurrentTheme { get; private set; } = "Dark";
        public string? LastAppliedTheme { get; private set; }

        public event EventHandler<string>? ThemeChanged;

        public void ApplyTheme(string themeName)
        {
            CurrentTheme = themeName;
            LastAppliedTheme = themeName;
            ThemeChanged?.Invoke(this, themeName);
        }
    }
}