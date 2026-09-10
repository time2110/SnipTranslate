using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SnipTranslate.Translation;

internal sealed class GoogleWebTranslationProvider : ITranslationProvider, IDisposable
{
    private static readonly string[] Endpoints =
    [
        "https://translate.googleapis.com/translate_a/single",
        "https://translate.google.com/translate_a/single"
    ];

    private readonly HttpClient _client;

    internal GoogleWebTranslationProvider(IWebProxy? proxy = null, bool useProxy = true)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            Proxy = proxy,
            UseProxy = useProxy
        };

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("SnipTranslate/0.1");
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var translatedChunks = new List<string>();
        foreach (var chunk in SplitText(text, 1500))
        {
            translatedChunks.Add(await TranslateChunkAsync(chunk, sourceLanguage, targetLanguage, cancellationToken));
        }

        return string.Join(Environment.NewLine, translatedChunks);
    }

    private async Task<string> TranslateChunkAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var rateLimited = false;

        foreach (var endpoint in Endpoints)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var url = endpoint +
                          $"?client=gtx&sl={Uri.EscapeDataString(sourceLanguage)}&tl={Uri.EscapeDataString(targetLanguage)}&dt=t" +
                          $"&q={Uri.EscapeDataString(text)}";
                using var response = await _client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    var segments = document.RootElement[0];
                    return string.Concat(
                        segments.EnumerateArray()
                            .Where(segment => segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0)
                            .Select(segment => segment[0].GetString()));
                }

                rateLimited |= response.StatusCode == HttpStatusCode.TooManyRequests;
                lastError = new HttpRequestException(
                    $"Google 翻译返回 {(int)response.StatusCode} ({response.ReasonPhrase})。",
                    null,
                    response.StatusCode);

                if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
                {
                    break;
                }

                var retryDelay = response.Headers.RetryAfter?.Delta ??
                                 TimeSpan.FromMilliseconds(attempt == 0 ? 450 : 900);
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds, 1500)), cancellationToken);
            }
        }

        if (rateLimited)
        {
            throw new InvalidOperationException(
                "Google 翻译请求过于频繁（429）。请稍后重试，或在设置中切换代理节点。OCR 原文仍可正常复制。",
                lastError);
        }

        throw new InvalidOperationException("暂时无法连接 Google 翻译，请检查网络或代理设置。", lastError);
    }

    private static IEnumerable<string> SplitText(string text, int maximumLength)
    {
        var remaining = text.Trim();
        while (remaining.Length > maximumLength)
        {
            var splitAt = remaining.LastIndexOfAny(
                ['\n', '\r', '。', '！', '？', '.', '!', '?', ' '],
                maximumLength - 1,
                maximumLength);
            if (splitAt < maximumLength / 2)
            {
                splitAt = maximumLength;
                if (char.IsHighSurrogate(remaining[splitAt - 1]))
                {
                    splitAt--;
                }
            }
            else
            {
                splitAt++;
            }

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    public void Dispose() => _client.Dispose();
}
