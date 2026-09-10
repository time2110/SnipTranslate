using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SnipTranslate.Settings;

internal sealed class AppSettingsStore
{
    private readonly object _sync = new();
    private readonly string _path;
    private AppSettings _settings;

    internal AppSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnipTranslate");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
        _settings = LoadCore();
    }

    internal AppSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _settings;
            }
        }
    }

    internal void Save(AppSettings settings)
    {
        var stored = new StoredSettings(
            settings.Proxy.Mode,
            settings.Proxy.Scheme,
            settings.Proxy.Host,
            settings.Proxy.Port,
            settings.Proxy.Username,
            Protect(settings.Proxy.Password),
            settings.TargetLanguage);

        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json, Encoding.UTF8);
        File.Move(temporaryPath, _path, overwrite: true);

        lock (_sync)
        {
            _settings = settings;
        }
    }

    private AppSettings LoadCore()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return AppSettings.Default;
            }

            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_path));
            if (stored is null)
            {
                return AppSettings.Default;
            }

            return new AppSettings(
                new ProxySettings(
                    stored.ProxyMode,
                    stored.ProxyScheme,
                    stored.ProxyHost,
                    stored.ProxyPort,
                    stored.ProxyUsername,
                    Unprotect(stored.ProtectedProxyPassword)),
                stored.TargetLanguage);
        }
        catch (Exception) when (File.Exists(_path))
        {
            return AppSettings.Default;
        }
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            null,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = ProtectedData.Unprotect(
            Convert.FromBase64String(value),
            null,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed record StoredSettings(
        ProxyMode ProxyMode,
        string ProxyScheme,
        string ProxyHost,
        int ProxyPort,
        string ProxyUsername,
        string ProtectedProxyPassword,
        string TargetLanguage);
}

