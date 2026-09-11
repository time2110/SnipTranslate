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
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnipTranslate");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
        _settings = LoadCore();
    }

    internal AppSettings Current
    {
        get { lock (_sync) return _settings; }
    }

    internal void Save(AppSettings settings)
    {
        var profiles = settings.Translation.Providers.Count > 0
            ? settings.Translation.Providers
            : AppSettings.Default.Translation.Providers;
        var first = profiles[0];
        var storedProfiles = profiles.Select(profile => new StoredTranslationProvider(
            profile.Id, profile.Name, profile.Kind, profile.Enabled, profile.Endpoint, Protect(profile.ApiKey),
            profile.Region, profile.ApiKeyHeader, profile.RequestTemplate, profile.ResponsePath)).ToArray();
        var stored = new StoredSettings(
            settings.Proxy.Mode, settings.Proxy.Scheme, settings.Proxy.Host, settings.Proxy.Port, settings.Proxy.Username,
            Protect(settings.Proxy.Password), settings.TargetLanguage,
            first.Kind, settings.Translation.EnableFallback, profiles.Select(profile => profile.Kind).ToArray(),
            first.Endpoint, Protect(first.ApiKey), first.Region, first.ApiKeyHeader, first.RequestTemplate, first.ResponsePath,
            storedProfiles);

        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json, Encoding.UTF8);
        File.Move(temporaryPath, _path, overwrite: true);
        lock (_sync) _settings = settings with
        {
            Translation = settings.Translation with { Providers = profiles }
        };
    }

    private AppSettings LoadCore()
    {
        try
        {
            if (!File.Exists(_path)) return AppSettings.Default;
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_path));
            if (stored is null) return AppSettings.Default;
            return new AppSettings(
                new ProxySettings(stored.ProxyMode, stored.ProxyScheme, stored.ProxyHost, stored.ProxyPort,
                    stored.ProxyUsername, Unprotect(stored.ProtectedProxyPassword)),
                stored.TargetLanguage,
                new TranslationSettings(stored.EnableTranslationFallback, LoadProfiles(stored)));
        }
        catch (Exception) when (File.Exists(_path))
        {
            return AppSettings.Default;
        }
    }

    private static IReadOnlyList<TranslationProviderSettings> LoadProfiles(StoredSettings stored)
    {
        if (stored.TranslationProviders is { Length: > 0 })
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            return stored.TranslationProviders.Select(item =>
            {
                var id = string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)
                    ? Guid.NewGuid().ToString("N")
                    : item.Id;
                var name = string.IsNullOrWhiteSpace(item.Name) ? AppSettings.ProviderDefaultName(item.Kind) : item.Name.Trim();
                return new TranslationProviderSettings(id, name, item.Kind, item.Enabled, item.Endpoint ?? string.Empty,
                    Unprotect(item.ProtectedApiKey ?? string.Empty), item.Region ?? string.Empty,
                    item.ApiKeyHeader ?? "Authorization",
                    item.RequestTemplate ?? "{\"q\":\"{text}\",\"source\":\"{source}\",\"target\":\"{target}\"}",
                    item.ResponsePath ?? "translatedText");
            }).ToArray();
        }

        var configured = AppSettings.CreateDefaultProvider(stored.TranslationProvider) with
        {
            Endpoint = stored.TranslationEndpoint ?? AppSettings.CreateDefaultProvider(stored.TranslationProvider).Endpoint,
            ApiKey = Unprotect(stored.ProtectedTranslationApiKey ?? string.Empty),
            Region = stored.TranslationRegion ?? string.Empty,
            ApiKeyHeader = stored.TranslationApiKeyHeader ?? "Authorization",
            RequestTemplate = stored.TranslationRequestTemplate ?? "{\"q\":\"{text}\",\"source\":\"{source}\",\"target\":\"{target}\"}",
            ResponsePath = stored.TranslationResponsePath ?? "translatedText"
        };
        var order = (stored.TranslationProviderOrder ?? [stored.TranslationProvider]).Distinct().ToList();
        if (!order.Contains(TranslationProviderKind.GoogleWeb)) order.Add(TranslationProviderKind.GoogleWeb);
        if (!order.Contains(TranslationProviderKind.MyMemory)) order.Add(TranslationProviderKind.MyMemory);
        return order
            .Where(kind => kind == configured.Kind || kind is TranslationProviderKind.GoogleWeb or TranslationProviderKind.MyMemory)
            .Select(kind => kind == configured.Kind ? configured : AppSettings.CreateDefaultProvider(kind))
            .ToArray();
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed record StoredSettings(
        ProxyMode ProxyMode,
        string ProxyScheme,
        string ProxyHost,
        int ProxyPort,
        string ProxyUsername,
        string ProtectedProxyPassword,
        string TargetLanguage,
        TranslationProviderKind TranslationProvider = TranslationProviderKind.GoogleWeb,
        bool EnableTranslationFallback = true,
        TranslationProviderKind[]? TranslationProviderOrder = null,
        string? TranslationEndpoint = null,
        string? ProtectedTranslationApiKey = null,
        string? TranslationRegion = null,
        string? TranslationApiKeyHeader = null,
        string? TranslationRequestTemplate = null,
        string? TranslationResponsePath = null,
        StoredTranslationProvider[]? TranslationProviders = null);

    private sealed record StoredTranslationProvider(
        string Id,
        string Name,
        TranslationProviderKind Kind,
        bool Enabled,
        string? Endpoint,
        string? ProtectedApiKey,
        string? Region,
        string? ApiKeyHeader,
        string? RequestTemplate,
        string? ResponsePath);
}
