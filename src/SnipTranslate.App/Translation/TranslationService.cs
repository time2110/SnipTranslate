using System.Net;
using System.Net.Http;
using System.Diagnostics;
using SnipTranslate.Settings;

namespace SnipTranslate.Translation;

internal sealed class TranslationService : ITranslationProvider, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private readonly Dictionary<string, ITranslationProvider> _providers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderHealth> _health = new(StringComparer.Ordinal);
    private AppSettings? _activeSettings;

    internal TranslationService(AppSettingsStore settingsStore) => _settingsStore = settingsStore;

    internal string LastProviderName { get; private set; } = string.Empty;
    internal bool LastFallbackOccurred { get; private set; }

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
        => (await TranslateDetailedAsync(text, sourceLanguage, targetLanguage, cancellationToken)).Text;

    internal async Task<TranslationResult> TranslateDetailedAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var settings = _settingsStore.Current;
        var profileSignature = string.Join(',', settings.Translation.Providers.Select(profile => profile.GetHashCode()));
        var cacheKey = $"{settings.Translation.EnableFallback}\u001f{profileSignature}\u001f{sourceLanguage}\u001f{targetLanguage}\u001f{text.Trim()}";
        lock (_sync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                LastProviderName = cached.ProviderName;
                LastFallbackOccurred = cached.WasFallback;
                return new TranslationResult(cached.Text, cached.ProviderName, cached.WasFallback, 0, true);
            }
        }

        var order = BuildProviderOrder(settings.Translation);
        var failures = new List<string>();
        for (var index = 0; index < order.Count; index++)
        {
            var profile = order[index];
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetCooldown(profile.Id, out var cooldown))
            {
                failures.Add($"{profile.Name}：暂时停用至 {cooldown:HH:mm:ss}");
                continue;
            }
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var translated = await GetProvider(profile).TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken);
                stopwatch.Stop();
                var entry = new CacheEntry(translated, profile.Name, index > 0);
                lock (_sync)
                {
                    _health.Remove(profile.Id);
                    LastProviderName = entry.ProviderName;
                    LastFallbackOccurred = entry.WasFallback;
                    if (!_cache.ContainsKey(cacheKey))
                    {
                        _cache[cacheKey] = entry;
                        _cacheOrder.Enqueue(cacheKey);
                        while (_cacheOrder.Count > 128) _cache.Remove(_cacheOrder.Dequeue());
                    }
                }
                return new TranslationResult(translated, profile.Name, index > 0, stopwatch.ElapsedMilliseconds, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RecordFailure(profile.Id, exception);
                failures.Add($"{profile.Name}：{ShortMessage(exception.Message)}");
            }
        }

        throw new InvalidOperationException("所有翻译接口均失败：" + string.Join("；", failures));
    }

    internal async Task<TranslationResult> TestProviderAsync(
        TranslationProviderSettings profile,
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var translated = await GetProvider(profile).TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken);
        stopwatch.Stop();
        return new TranslationResult(translated, profile.Name, false, stopwatch.ElapsedMilliseconds, false);
    }

    private bool TryGetCooldown(string providerId, out DateTimeOffset until)
    {
        lock (_sync)
        {
            if (_health.TryGetValue(providerId, out var health) && health.UnavailableUntil > DateTimeOffset.Now)
            {
                until = health.UnavailableUntil;
                return true;
            }
            until = default;
            return false;
        }
    }

    private void RecordFailure(string providerId, Exception exception)
    {
        lock (_sync)
        {
            _health.TryGetValue(providerId, out var previous);
            var count = previous?.ConsecutiveFailures + 1 ?? 1;
            var cooldown = exception switch
            {
                HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => TimeSpan.FromMinutes(1),
                HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => TimeSpan.FromMinutes(5),
                _ when count >= 2 => TimeSpan.FromSeconds(30),
                _ => TimeSpan.Zero
            };
            _health[providerId] = new ProviderHealth(count, DateTimeOffset.Now + cooldown);
        }
    }

    private static IReadOnlyList<TranslationProviderSettings> BuildProviderOrder(TranslationSettings settings)
    {
        var enabled = settings.Providers.Where(profile => profile.Enabled).ToList();
        if (enabled.Count == 0) throw new InvalidOperationException("没有启用的翻译 API，请先在设置中启用至少一项。");
        return settings.EnableFallback ? enabled : [enabled[0]];
    }

    private ITranslationProvider GetProvider(TranslationProviderSettings profile)
    {
        lock (_sync)
        {
            var settings = _settingsStore.Current;
            if (settings != _activeSettings)
            {
                DisposeProviders();
                _activeSettings = settings;
            }
            if (_providers.TryGetValue(profile.Id, out var existing)) return existing;

            var proxy = settings.Proxy.Mode == ProxyMode.Custom ? CreateProxy(settings.Proxy) : null;
            var useProxy = settings.Proxy.Mode != ProxyMode.Direct;
            ITranslationProvider created = profile.Kind switch
            {
                TranslationProviderKind.MicrosoftTranslator => new MicrosoftTranslationProvider(profile, proxy, useProxy),
                TranslationProviderKind.LibreTranslate => new LibreTranslateProvider(profile, proxy, useProxy),
                TranslationProviderKind.MyMemory => new MyMemoryTranslationProvider(profile, proxy, useProxy),
                TranslationProviderKind.Custom => new CustomTranslationProvider(profile, proxy, useProxy),
                _ => new GoogleWebTranslationProvider(proxy, useProxy)
            };
            _providers[profile.Id] = created;
            return created;
        }
    }

    internal static string ProviderName(TranslationProviderKind kind) => kind switch
    {
        TranslationProviderKind.GoogleWeb => "Google Web",
        TranslationProviderKind.MicrosoftTranslator => "微软/Bing",
        TranslationProviderKind.LibreTranslate => "LibreTranslate",
        TranslationProviderKind.MyMemory => "MyMemory",
        _ => "自定义 API"
    };

    private static string ShortMessage(string message)
    {
        var normalized = message.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100] + "…";
    }

    private static IWebProxy CreateProxy(ProxySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is <= 0 or > 65535)
            throw new InvalidOperationException("自定义代理地址或端口无效。");
        var proxy = new WebProxy(new Uri($"{settings.Scheme}://{settings.Host}:{settings.Port}"));
        if (!string.IsNullOrWhiteSpace(settings.Username))
            proxy.Credentials = new NetworkCredential(settings.Username, settings.Password);
        return proxy;
    }

    private void DisposeProviders()
    {
        foreach (var provider in _providers.Values.OfType<IDisposable>()) provider.Dispose();
        _providers.Clear();
    }

    public void Dispose()
    {
        lock (_sync) DisposeProviders();
    }

    private sealed record CacheEntry(string Text, string ProviderName, bool WasFallback);
    private sealed record ProviderHealth(int ConsecutiveFailures, DateTimeOffset UnavailableUntil);
}

internal sealed record TranslationResult(
    string Text,
    string ProviderName,
    bool WasFallback,
    long ElapsedMilliseconds,
    bool FromCache);
