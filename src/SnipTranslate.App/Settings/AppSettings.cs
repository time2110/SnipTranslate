namespace SnipTranslate.Settings;

internal enum ProxyMode
{
    System,
    Direct,
    Custom
}

internal sealed record ProxySettings(
    ProxyMode Mode,
    string Scheme,
    string Host,
    int Port,
    string Username,
    string Password);

internal enum TranslationProviderKind
{
    GoogleWeb,
    MicrosoftTranslator,
    LibreTranslate,
    MyMemory,
    Custom
}

internal sealed record TranslationProviderSettings(
    string Id,
    string Name,
    TranslationProviderKind Kind,
    bool Enabled,
    string Endpoint,
    string ApiKey,
    string Region,
    string ApiKeyHeader,
    string RequestTemplate,
    string ResponsePath);

internal sealed record TranslationSettings(
    bool EnableFallback,
    IReadOnlyList<TranslationProviderSettings> Providers);

internal sealed record HotkeySettings(
    uint CaptureModifiers,
    uint CaptureVirtualKey,
    uint TranslateModifiers,
    uint TranslateVirtualKey);

internal sealed record OcrSettings(
    string Language,
    bool EnableAngleDetection);

internal sealed record AppSettings(
    ProxySettings Proxy,
    string TargetLanguage,
    TranslationSettings Translation,
    HotkeySettings Hotkeys,
    OcrSettings Ocr)
{
    internal static AppSettings Default { get; } = new(
        new ProxySettings(ProxyMode.System, "http", string.Empty, 7890, string.Empty, string.Empty),
        "zh-CN",
        new TranslationSettings(
            true,
            new[]
            {
                CreateDefaultProvider(TranslationProviderKind.GoogleWeb),
                CreateDefaultProvider(TranslationProviderKind.MyMemory)
            }),
        new HotkeySettings(0, 0x70, 0x0002, 0x70),
        new OcrSettings("auto", true));

    internal static TranslationProviderSettings CreateDefaultProvider(TranslationProviderKind kind) => new(
        Guid.NewGuid().ToString("N"),
        ProviderDefaultName(kind),
        kind,
        true,
        kind switch
        {
            TranslationProviderKind.MicrosoftTranslator => "https://api.cognitive.microsofttranslator.com",
            TranslationProviderKind.LibreTranslate => "https://libretranslate.com",
            TranslationProviderKind.MyMemory => "https://api.mymemory.translated.net/get",
            _ => string.Empty
        },
        string.Empty,
        string.Empty,
        "Authorization",
        "{\"q\":\"{text}\",\"source\":\"{source}\",\"target\":\"{target}\"}",
        "translatedText");

    internal static string ProviderDefaultName(TranslationProviderKind kind) => kind switch
    {
        TranslationProviderKind.GoogleWeb => "Google Web",
        TranslationProviderKind.MicrosoftTranslator => "微软/Bing Translator",
        TranslationProviderKind.LibreTranslate => "LibreTranslate",
        TranslationProviderKind.MyMemory => "MyMemory",
        _ => "自定义 API"
    };
}
