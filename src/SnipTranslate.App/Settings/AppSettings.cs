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

internal sealed record AppSettings(
    ProxySettings Proxy,
    string TargetLanguage)
{
    internal static AppSettings Default { get; } = new(
        new ProxySettings(ProxyMode.System, "http", string.Empty, 7890, string.Empty, string.Empty),
        "zh-CN");
}

