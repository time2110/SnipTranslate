using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SnipTranslate.Translation;

namespace SnipTranslate.Settings;

public partial class SettingsWindow : Window
{
    private readonly AppSettingsStore _store;

    internal SettingsWindow(AppSettingsStore store)
    {
        InitializeComponent();
        _store = store;
        LoadSettings(store.Current);
    }

    private void LoadSettings(AppSettings settings)
    {
        ProxyModeBox.SelectedIndex = (int)settings.Proxy.Mode;
        ProxySchemeBox.SelectedIndex = settings.Proxy.Scheme switch
        {
            "https" => 1,
            "socks5" => 2,
            _ => 0
        };
        ProxyHostBox.Text = settings.Proxy.Host;
        ProxyPortBox.Text = settings.Proxy.Port.ToString();
        ProxyUsernameBox.Text = settings.Proxy.Username;
        ProxyPasswordBox.Password = settings.Proxy.Password;
        TargetLanguageBox.SelectedItem = TargetLanguageBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, settings.TargetLanguage));
        TargetLanguageBox.SelectedIndex = Math.Max(0, TargetLanguageBox.SelectedIndex);
        UpdateCustomProxyFields();
    }

    private AppSettings ReadSettings()
    {
        var mode = (ProxyMode)Math.Max(0, ProxyModeBox.SelectedIndex);
        var scheme = ((ComboBoxItem?)ProxySchemeBox.SelectedItem)?.Content?.ToString() ?? "http";
        if (!int.TryParse(ProxyPortBox.Text, out var port) || port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("代理端口必须是 1 到 65535 之间的数字。");
        }

        var target = ((ComboBoxItem?)TargetLanguageBox.SelectedItem)?.Tag?.ToString() ?? "zh-CN";
        return new AppSettings(
            new ProxySettings(
                mode,
                scheme,
                ProxyHostBox.Text.Trim(),
                port,
                ProxyUsernameBox.Text.Trim(),
                ProxyPasswordBox.Password),
            target);
    }

    private void OnProxyModeChanged(object sender, SelectionChangedEventArgs e) => UpdateCustomProxyFields();

    private void UpdateCustomProxyFields()
    {
        if (ProxySchemeBox is null)
        {
            return;
        }

        var enabled = ProxyModeBox.SelectedIndex == (int)ProxyMode.Custom;
        ProxySchemeBox.IsEnabled = enabled;
        ProxyHostBox.IsEnabled = enabled;
        ProxyPortBox.IsEnabled = enabled;
        ProxyUsernameBox.IsEnabled = enabled;
        ProxyPasswordBox.IsEnabled = enabled;
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            _store.Save(settings);
            TestButton.IsEnabled = false;
            StatusText.Text = "正在测试 Google 翻译…";
            var timer = Stopwatch.StartNew();
            using var service = new TranslationService(_store);
            var result = await service.TranslateAsync("hello", "auto", settings.TargetLanguage, CancellationToken.None);
            timer.Stop();
            StatusText.Text = $"连接成功 · {timer.ElapsedMilliseconds} ms · {result}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"连接失败：{exception.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.Save(ReadSettings());
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
