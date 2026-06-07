using System.Text.Json;

namespace DebtManager.Infrastructure.Security;

/// <summary>
/// Secure configuration storage for sensitive settings.
/// All values are encrypted at rest.
/// </summary>
public sealed class SecureConfiguration
{
    private readonly string _configPath;
    private readonly PayloadEncryptor _encryptor;
    private readonly Dictionary<string, string> _cache = new();
    private readonly object _lock = new();

    public SecureConfiguration(IKeyStore keyStore, string? configPath = null)
    {
        _encryptor = new PayloadEncryptor(keyStore);
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager",
            "secure_config.json");
    }

    /// <summary>
    /// Get a configuration value.
    /// </summary>
    public string? Get(string key)
    {
        lock (_lock)
        {
            LoadIfNeeded();
            return _cache.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Set a configuration value (encrypted at rest).
    /// </summary>
    public void Set(string key, string value)
    {
        lock (_lock)
        {
            LoadIfNeeded();
            _cache[key] = value;
            Save();
        }
    }

    /// <summary>
    /// Remove a configuration value.
    /// </summary>
    public void Remove(string key)
    {
        lock (_lock)
        {
            LoadIfNeeded();
            _cache.Remove(key);
            Save();
        }
    }

    /// <summary>
    /// Check if a key exists.
    /// </summary>
    public bool Contains(string key)
    {
        lock (_lock)
        {
            LoadIfNeeded();
            return _cache.ContainsKey(key);
        }
    }

    private bool _loaded;

    private void LoadIfNeeded()
    {
        if (_loaded) return;

        if (File.Exists(_configPath))
        {
            try
            {
                var encrypted = File.ReadAllText(_configPath);
                var json = _encryptor.Decrypt(encrypted);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                        _cache[kvp.Key] = kvp.Value;
                }
            }
            catch
            {
                // Config corrupted or key changed - start fresh
                _cache.Clear();
            }
        }

        _loaded = true;
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_cache);
        var encrypted = _encryptor.Encrypt(json);
        File.WriteAllText(_configPath, encrypted);
    }
}

/// <summary>
/// Standard configuration keys for the application.
/// </summary>
public static class ConfigKeys
{
    // Sync configuration
    public const string SyncApiKey = "sync:api_key";
    public const string SyncBaseUrl = "sync:base_url";
    public const string SyncVaultId = "sync:vault_id";
    public const string SyncEnabled = "sync:enabled";

    // Security settings
    public const string AutoLockMinutes = "security:auto_lock_minutes";
    public const string RequirePasswordOnStart = "security:require_password_on_start";
    public const string RequireAppUnlock = "security:require_app_unlock";
    public const string MaxLoginAttempts = "security:max_login_attempts";

    // User preferences (non-sensitive, but encrypted anyway)
    public const string DefaultCurrency = "prefs:default_currency";
    public const string DefaultTimeZone = "prefs:default_timezone";
    public const string DateFormat = "prefs:date_format";
    public const string LastSyncTime = "prefs:last_sync_time";

    // Onboarding
    public const string HasCompletedOnboarding = "app:has_completed_onboarding";

    // Appearance
    public const string AppTheme = "prefs:app_theme";
}
