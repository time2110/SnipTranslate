using System.Net;
using SnipTranslate.Settings;

namespace SnipTranslate.Translation;

internal sealed class TranslationService : ITranslationProvider, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private GoogleWebTranslationProvider? _provider;
    private ProxySettings? _activeProxy;

    internal TranslationService(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{sourceLanguage}\u001f{targetLanguage}\u001f{text.Trim()}";
        lock (_sync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var translated = await GetProvider().TranslateAsync(
            text,
            sourceLanguage,
            targetLanguage,
            cancellationToken);

        lock (_sync)
        {
            if (!_cache.ContainsKey(cacheKey))
            {
                _cache[cacheKey] = translated;
                _cacheOrder.Enqueue(cacheKey);
                while (_cacheOrder.Count > 128)
                {
                    _cache.Remove(_cacheOrder.Dequeue());
                }
            }
        }

        return translated;
    }

    private GoogleWebTranslationProvider GetProvider()
    {
        lock (_sync)
        {
            var proxySettings = _settingsStore.Current.Proxy;
            if (_provider is not null && proxySettings == _activeProxy)
            {
                return _provider;
            }

            _provider?.Dispose();
            _activeProxy = proxySettings;
            _provider = proxySettings.Mode switch
            {
                ProxyMode.Direct => new GoogleWebTranslationProvider(null, useProxy: false),
                ProxyMode.Custom => new GoogleWebTranslationProvider(CreateProxy(proxySettings), useProxy: true),
                _ => new GoogleWebTranslationProvider(null, useProxy: true)
            };
            return _provider;
        }
    }

    private static IWebProxy CreateProxy(ProxySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("自定义代理地址或端口无效。");
        }

        var proxy = new WebProxy(new Uri($"{settings.Scheme}://{settings.Host}:{settings.Port}"));
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            proxy.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        return proxy;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _provider?.Dispose();
            _provider = null;
        }
    }
}
