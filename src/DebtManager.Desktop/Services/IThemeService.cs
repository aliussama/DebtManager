namespace DebtManager.Desktop.Services;

/// <summary>
/// Service for managing app-wide theme switching.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// The currently active theme name ("Dark" or "Light").
    /// </summary>
    string CurrentTheme { get; }

    /// <summary>
    /// Applies the specified theme by swapping merged resource dictionaries.
    /// Persists the choice to SecureConfiguration.
    /// </summary>
    void ApplyTheme(string themeName);

    /// <summary>
    /// Fired after a theme has been applied.
    /// </summary>
    event EventHandler<string>? ThemeChanged;
}
