using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SnipTranslate.Settings;

namespace SnipTranslate.Translation;

internal abstract class HttpTranslationProvider : ITranslationProvider, IDisposable
{
    protected HttpTranslationProvider(IWebProxy? proxy, bool useProxy)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            Proxy = proxy,
            UseProxy = useProxy
        };
        Client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("SnipTranslate/0.1");
    }

    protected HttpClient Client { get; }

    public abstract Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);

    protected static string NormalizeLanguage(string language, bool microsoft = false)
    {
        return language switch
        {
            "zh-CN" => microsoft ? "zh-Hans" : "zh",
            "zh-TW" => microsoft ? "zh-Hant" : "zh-TW",
            _ => language
        };
    }

    protected static Uri RequireEndpoint(string endpoint, string defaultEndpoint)
    {
        var value = string.IsNullOrWhiteSpace(endpoint) ? defaultEndpoint : endpoint.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("翻译 API 地址必须是有效的 HTTP/HTTPS 地址。");
        }

        return uri;
    }

    protected static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        string providerName,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = content.Length > 240 ? content[..240] + "…" : content;
            throw new HttpRequestException(
                $"{providerName} 返回 {(int)response.StatusCode} ({response.ReasonPhrase})：{detail}",
                null,
                response.StatusCode);
        }

        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{providerName} 返回的不是有效 JSON。", exception);
        }
    }

    public void Dispose() => Client.Dispose();
}

internal sealed class MicrosoftTranslationProvider : HttpTranslationProvider
{
    private readonly TranslationProviderSettings _settings;

    internal MicrosoftTranslationProvider(TranslationProviderSettings settings, IWebProxy? proxy, bool useProxy)
        : base(proxy, useProxy) => _settings = settings;

    public override async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("微软/Bing Translator 需要填写 Azure Translator API Key。");
        }

        var endpoint = RequireEndpoint(_settings.Endpoint, "https://api.cognitive.microsofttranslator.com");
        var baseUrl = endpoint.AbsoluteUri.TrimEnd('/');
        var translateUrl = baseUrl.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : endpoint.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase) && endpoint.AbsolutePath == "/"
                ? baseUrl + "/translator/text/v3.0/translate"
                : baseUrl + "/translate";
        var separator = translateUrl.Contains('?') ? "&" : "?";
        var url = $"{translateUrl}{separator}api-version=3.0&to={Uri.EscapeDataString(NormalizeLanguage(targetLanguage, true))}";
        if (!string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            url += $"&from={Uri.EscapeDataString(NormalizeLanguage(sourceLanguage, true))}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _settings.ApiKey.Trim());
        if (!string.IsNullOrWhiteSpace(_settings.Region))
        {
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", _settings.Region.Trim());
        }
        request.Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }), Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, cancellationToken);
        using var document = await ReadJsonAsync(response, "微软/Bing Translator", cancellationToken);
        try
        {
            return document.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? string.Empty;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException("微软/Bing Translator 响应中没有找到译文。", exception);
        }
    }
}

internal sealed class LibreTranslateProvider : HttpTranslationProvider
{
    private readonly TranslationProviderSettings _settings;

    internal LibreTranslateProvider(TranslationProviderSettings settings, IWebProxy? proxy, bool useProxy)
        : base(proxy, useProxy) => _settings = settings;

    public override async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        var endpoint = RequireEndpoint(_settings.Endpoint, "https://libretranslate.com");
        var url = endpoint.AbsolutePath.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)
            ? endpoint.AbsoluteUri
            : endpoint.AbsoluteUri.TrimEnd('/') + "/translate";
        var body = new Dictionary<string, object?>
        {
            ["q"] = text,
            ["source"] = NormalizeLibreLanguage(sourceLanguage),
            ["target"] = NormalizeLibreLanguage(targetLanguage),
            ["format"] = "text"
        };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            body["api_key"] = _settings.ApiKey.Trim();
        }

        using var response = await Client.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            cancellationToken);
        using var document = await ReadJsonAsync(response, "LibreTranslate", cancellationToken);
        if (document.RootElement.TryGetProperty("translatedText", out var result))
        {
            return result.GetString() ?? string.Empty;
        }
        throw new InvalidOperationException("LibreTranslate 响应中没有找到 translatedText。");
    }

    private static string NormalizeLibreLanguage(string language) => language is "zh-CN" or "zh-TW" ? "zh" : language;
}

internal sealed class MyMemoryTranslationProvider : HttpTranslationProvider
{
    private readonly TranslationProviderSettings _settings;

    internal MyMemoryTranslationProvider(TranslationProviderSettings settings, IWebProxy? proxy, bool useProxy)
        : base(proxy, useProxy) => _settings = settings;

    public override async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MyMemory 需要明确的源语言，请在 OCR 卡片中选择源语言。");
        }

        var results = new List<string>();
        foreach (var chunk in SplitUtf8(text.Trim(), 450))
        {
            var endpoint = RequireEndpoint(_settings.Endpoint, "https://api.mymemory.translated.net/get");
            var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
            var url = endpoint.AbsoluteUri + separator +
                      $"q={Uri.EscapeDataString(chunk)}&langpair={Uri.EscapeDataString(NormalizeLanguage(sourceLanguage) + "|" + NormalizeLanguage(targetLanguage))}";
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                url += $"&key={Uri.EscapeDataString(_settings.ApiKey.Trim())}";
            }

            using var response = await Client.GetAsync(url, cancellationToken);
            using var document = await ReadJsonAsync(response, "MyMemory", cancellationToken);
            if (!document.RootElement.TryGetProperty("responseData", out var data) ||
                !data.TryGetProperty("translatedText", out var translated))
            {
                throw new InvalidOperationException("MyMemory 响应中没有找到译文。");
            }
            results.Add(WebUtility.HtmlDecode(translated.GetString() ?? string.Empty));
        }
        return string.Join(Environment.NewLine, results);
    }

    private static IEnumerable<string> SplitUtf8(string text, int maximumBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (bytes + runeBytes > maximumBytes && builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
                bytes = 0;
            }
            builder.Append(rune.ToString());
            bytes += runeBytes;
        }
        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }
}

internal sealed class CustomTranslationProvider : HttpTranslationProvider
{
    private readonly TranslationProviderSettings _settings;

    internal CustomTranslationProvider(TranslationProviderSettings settings, IWebProxy? proxy, bool useProxy)
        : base(proxy, useProxy) => _settings = settings;

    public override async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        var endpoint = RequireEndpoint(_settings.Endpoint, string.Empty);
        var body = ExpandTemplate(_settings.RequestTemplate, text, sourceLanguage, targetLanguage);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            var header = string.IsNullOrWhiteSpace(_settings.ApiKeyHeader) ? "Authorization" : _settings.ApiKeyHeader.Trim();
            if (!request.Headers.TryAddWithoutValidation(header, _settings.ApiKey.Trim()))
            {
                throw new InvalidOperationException("自定义 API 的 Key 请求头名称无效。");
            }
        }

        using var response = await Client.SendAsync(request, cancellationToken);
        using var document = await ReadJsonAsync(response, "自定义翻译 API", cancellationToken);
        var value = ResolvePath(document.RootElement, _settings.ResponsePath);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static string ExpandTemplate(string template, string text, string source, string target)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException("自定义 API 请求模板不能为空。");
        }
        static string Escape(string value)
        {
            var json = JsonSerializer.Serialize(value);
            return json[1..^1];
        }
        var result = template
            .Replace("{text}", Escape(text), StringComparison.Ordinal)
            .Replace("{source}", Escape(source), StringComparison.Ordinal)
            .Replace("{target}", Escape(target), StringComparison.Ordinal);
        try
        {
            using var _ = JsonDocument.Parse(result);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("自定义 API 请求模板生成的 JSON 无效。", exception);
        }
        return result;
    }

    private static JsonElement ResolvePath(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("自定义 API 响应路径不能为空。");
        }
        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(segment, out var index) && current.ValueKind == JsonValueKind.Array && index >= 0 && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var property))
            {
                current = property;
            }
            else
            {
                throw new InvalidOperationException($"自定义 API 响应中找不到路径：{path}");
            }
        }
        return current;
    }
}
