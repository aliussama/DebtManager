using DebtManager.Infrastructure.Security;

namespace DebtManager.Desktop.Services;

/// <summary>
/// Applies Dark or Light theme by swapping a merged ResourceDictionary
/// at the Application level. Persists choice to SecureConfiguration.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly SecureConfiguration _config;
    private System.Windows.ResourceDictionary? _currentThemeDict;

    public ThemeService(SecureConfiguration config)
    {
        _config = config;

        // Read persisted theme, default to Dark
        var saved = _config.Get(ConfigKeys.AppTheme);
        CurrentTheme = NormalizeThemeName(saved);
    }

    public string CurrentTheme { get; private set; }

    public event EventHandler<string>? ThemeChanged;

    public void ApplyTheme(string themeName)
    {
        var normalized = NormalizeThemeName(themeName);

        var app = System.Windows.Application.Current;
        if (app == null) return;

        // Build the pack URI for the theme dictionary
        var uri = new Uri($"pack://application:,,,/Themes/Theme.{normalized}.xaml", UriKind.Absolute);
        var newDict = new System.Windows.ResourceDictionary { Source = uri };

        // Remove old theme dictionary if present
        if (_currentThemeDict != null)
        {
            app.Resources.MergedDictionaries.Remove(_currentThemeDict);
        }

        // Add the new one
        app.Resources.MergedDictionaries.Add(newDict);
        _currentThemeDict = newDict;

        CurrentTheme = normalized;

        // Persist
        _config.Set(ConfigKeys.AppTheme, normalized);

        ThemeChanged?.Invoke(this, normalized);
    }

    private static string NormalizeThemeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Dark";

        if (name.Equals("Light", StringComparison.OrdinalIgnoreCase))
            return "Light";

        return "Dark";
    }
}
