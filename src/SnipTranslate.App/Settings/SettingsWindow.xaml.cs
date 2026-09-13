using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnipTranslate.Translation;

namespace SnipTranslate.Settings;

public partial class SettingsWindow : Window
{
    private readonly AppSettingsStore _store;
    private bool _loading;
    private TranslationProviderKind _displayedProvider;
    private ListBoxItem? _editingProviderItem;
    private Point _providerDragStart;
    private ListBoxItem? _draggedProviderItem;
    private HotkeySettings _hotkeys = AppSettings.Default.Hotkeys;

    internal SettingsWindow(AppSettingsStore store)
    {
        InitializeComponent();
        _store = store;
        LoadSettings(store.Current);
    }

    private void LoadSettings(AppSettings settings)
    {
        _loading = true;
        StartupBox.IsChecked = StartupManager.IsEnabled();
        _hotkeys = settings.Hotkeys;
        CaptureHotkeyBox.Text = FormatHotkey(_hotkeys.CaptureModifiers, _hotkeys.CaptureVirtualKey);
        TranslateHotkeyBox.Text = FormatHotkey(_hotkeys.TranslateModifiers, _hotkeys.TranslateVirtualKey);
        OcrLanguageBox.SelectedItem = OcrLanguageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, settings.Ocr.Language));
        OcrLanguageBox.SelectedIndex = Math.Max(0, OcrLanguageBox.SelectedIndex);
        OcrAngleBox.IsChecked = settings.Ocr.EnableAngleDetection;
        OcrQualityBox.SelectedItem = OcrQualityBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, settings.Ocr.Quality));
        OcrQualityBox.SelectedIndex = Math.Max(0, OcrQualityBox.SelectedIndex);
        FallbackBox.IsChecked = settings.Translation.EnableFallback;
        ProviderOrderBox.Items.Clear();
        foreach (var profile in settings.Translation.Providers) ProviderOrderBox.Items.Add(CreateProviderItem(profile));
        UpdateProviderCount();
        ProxyModeBox.SelectedIndex = (int)settings.Proxy.Mode;
        ProxySchemeBox.SelectedIndex = settings.Proxy.Scheme switch { "https" => 1, "socks5" => 2, _ => 0 };
        ProxyHostBox.Text = settings.Proxy.Host;
        ProxyPortBox.Text = settings.Proxy.Port.ToString();
        ProxyUsernameBox.Text = settings.Proxy.Username;
        ProxyPasswordBox.Password = settings.Proxy.Password;
        TargetLanguageBox.SelectedItem = TargetLanguageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, settings.TargetLanguage));
        TargetLanguageBox.SelectedIndex = Math.Max(0, TargetLanguageBox.SelectedIndex);
        _loading = false;
        if (ProviderOrderBox.Items.Count > 0)
        {
            ProviderOrderBox.SelectedIndex = 0;
        }
        UpdateCustomProxyFields();
    }

    private AppSettings ReadSettings()
    {
        CommitProviderEditor();
        var mode = (ProxyMode)Math.Max(0, ProxyModeBox.SelectedIndex);
        var scheme = ((ComboBoxItem?)ProxySchemeBox.SelectedItem)?.Content?.ToString() ?? "http";
        if (!int.TryParse(ProxyPortBox.Text, out var port) || port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("代理端口必须是 1 到 65535 之间的数字。");
        }
        var providers = ReadProviderProfiles();
        if (providers.Count == 0) throw new InvalidOperationException("至少需要添加一个翻译 API。");
        if (!providers.Any(profile => profile.Enabled)) throw new InvalidOperationException("至少需要启用一个翻译 API。");
        if (_hotkeys.CaptureModifiers == _hotkeys.TranslateModifiers &&
            _hotkeys.CaptureVirtualKey == _hotkeys.TranslateVirtualKey)
            throw new InvalidOperationException("截图和截图翻译不能使用相同的快捷键。");
        var target = ((ComboBoxItem?)TargetLanguageBox.SelectedItem)?.Tag?.ToString() ?? "zh-CN";
        return new AppSettings(
            new ProxySettings(mode, scheme, ProxyHostBox.Text.Trim(), port, ProxyUsernameBox.Text.Trim(), ProxyPasswordBox.Password), target,
            new TranslationSettings(FallbackBox.IsChecked == true, providers),
            _hotkeys,
            new OcrSettings(
                (OcrLanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
                OcrAngleBox.IsChecked == true,
                (OcrQualityBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "fast"));
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var kind = (TranslationProviderKind)Math.Max(0, ProviderBox.SelectedIndex);
        var previousDefault = DefaultEndpoint(_displayedProvider);
        if (string.IsNullOrWhiteSpace(EndpointBox.Text) ||
            string.Equals(EndpointBox.Text.TrimEnd('/'), previousDefault.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            EndpointBox.Text = DefaultEndpoint(kind);
        }
        if (string.IsNullOrWhiteSpace(ProviderNameBox.Text) ||
            string.Equals(ProviderNameBox.Text, AppSettings.ProviderDefaultName(_displayedProvider), StringComparison.Ordinal))
        {
            ProviderNameBox.Text = AppSettings.ProviderDefaultName(kind);
        }
        _displayedProvider = kind;
        UpdateProviderFields();
    }

    private void UpdateProviderFields()
    {
        if (EndpointRow is null) return;
        var kind = (TranslationProviderKind)Math.Max(0, ProviderBox.SelectedIndex);
        EndpointRow.Visibility = kind == TranslationProviderKind.GoogleWeb ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyRow.Visibility = kind == TranslationProviderKind.GoogleWeb ? Visibility.Collapsed : Visibility.Visible;
        RegionRow.Visibility = kind == TranslationProviderKind.MicrosoftTranslator ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyHeaderRow.Visibility = kind == TranslationProviderKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        RequestTemplateRow.Visibility = kind == TranslationProviderKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        ResponsePathRow.Visibility = kind == TranslationProviderKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyLabel.Text = kind is TranslationProviderKind.LibreTranslate or TranslationProviderKind.MyMemory ? "API Key（可选）" : "API Key";
        ProviderHint.Text = kind switch
        {
            TranslationProviderKind.GoogleWeb => "无需 Key，开箱即用；公共 Web 接口可能出现 429 限流。",
            TranslationProviderKind.MicrosoftTranslator => "填写 Azure Translator F0/付费资源的 Key；区域资源还需填写 Region。",
            TranslationProviderKind.LibreTranslate => "可使用公共实例或自建服务。部分公共实例需要 API Key。",
            TranslationProviderKind.MyMemory => "无需 Key 即可使用，但有额度限制；长文本会自动按 UTF-8 字节拆分。",
            _ => "POST JSON。模板支持 {text}、{source}、{target}；响应路径支持点号和数组下标。"
        };
        if (kind == TranslationProviderKind.Custom)
        {
            ApiKeyHeaderBox.Text = string.IsNullOrWhiteSpace(ApiKeyHeaderBox.Text) ? "Authorization" : ApiKeyHeaderBox.Text;
            RequestTemplateBox.Text = string.IsNullOrWhiteSpace(RequestTemplateBox.Text) ? "{\"q\":\"{text}\",\"source\":\"{source}\",\"target\":\"{target}\"}" : RequestTemplateBox.Text;
            ResponsePathBox.Text = string.IsNullOrWhiteSpace(ResponsePathBox.Text) ? "translatedText" : ResponsePathBox.Text;
        }
    }

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        CommitProviderEditor();
        _editingProviderItem = ProviderOrderBox.SelectedItem as ListBoxItem;
        if (_editingProviderItem?.Tag is TranslationProviderSettings profile) LoadProviderEditor(profile);
    }

    private void LoadProviderEditor(TranslationProviderSettings profile)
    {
        _loading = true;
        ProviderNameBox.Text = profile.Name;
        ProviderBox.SelectedIndex = (int)profile.Kind;
        ProviderEnabledBox.IsChecked = profile.Enabled;
        EndpointBox.Text = profile.Endpoint;
        ApiKeyBox.Password = profile.ApiKey;
        RegionBox.Text = profile.Region;
        ApiKeyHeaderBox.Text = profile.ApiKeyHeader;
        RequestTemplateBox.Text = profile.RequestTemplate;
        ResponsePathBox.Text = profile.ResponsePath;
        _displayedProvider = profile.Kind;
        _loading = false;
        UpdateProviderFields();
    }

    private void CommitProviderEditor()
    {
        if (_loading || _editingProviderItem?.Tag is not TranslationProviderSettings existing) return;
        var kind = (TranslationProviderKind)Math.Max(0, ProviderBox.SelectedIndex);
        var name = string.IsNullOrWhiteSpace(ProviderNameBox.Text) ? AppSettings.ProviderDefaultName(kind) : ProviderNameBox.Text.Trim();
        var updated = existing with
        {
            Name = name,
            Kind = kind,
            Enabled = ProviderEnabledBox.IsChecked == true,
            Endpoint = EndpointBox.Text.Trim(),
            ApiKey = ApiKeyBox.Password,
            Region = RegionBox.Text.Trim(),
            ApiKeyHeader = ApiKeyHeaderBox.Text.Trim(),
            RequestTemplate = RequestTemplateBox.Text,
            ResponsePath = ResponsePathBox.Text.Trim()
        };
        _editingProviderItem.Tag = updated;
        _editingProviderItem.Content = ProviderItemText(updated);
    }

    private IReadOnlyList<TranslationProviderSettings> ReadProviderProfiles() => ProviderOrderBox.Items
        .OfType<ListBoxItem>().Select(item => (TranslationProviderSettings)item.Tag).ToArray();

    private static ListBoxItem CreateProviderItem(TranslationProviderSettings profile) => new()
    {
        Tag = profile,
        Content = ProviderItemText(profile),
        Padding = new Thickness(7, 5, 7, 5),
        Cursor = Cursors.SizeNS
    };

    private static string ProviderItemText(TranslationProviderSettings profile)
    {
        var typeName = TranslationService.ProviderName(profile.Kind);
        var isDefaultName = string.Equals(profile.Name, AppSettings.ProviderDefaultName(profile.Kind), StringComparison.Ordinal) ||
                            string.Equals(profile.Name, typeName, StringComparison.Ordinal);
        var label = isDefaultName ? profile.Name : $"{profile.Name} · {typeName}";
        return $"☷  {(profile.Enabled ? "●" : "○")}  {label}";
    }

    private void OnAddProviderClick(object sender, RoutedEventArgs e)
    {
        CommitProviderEditor();
        var profile = AppSettings.CreateDefaultProvider(TranslationProviderKind.Custom) with { Name = "新翻译接口" };
        var item = CreateProviderItem(profile);
        ProviderOrderBox.Items.Add(item);
        UpdateProviderCount();
        ProviderOrderBox.SelectedItem = item;
        ProviderOrderBox.ScrollIntoView(item);
        ProviderNameBox.Focus();
        ProviderNameBox.SelectAll();
    }

    private void OnApplyProviderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            _store.Save(settings);
            var savedName = _editingProviderItem?.Tag is TranslationProviderSettings profile ? profile.Name : "接口";
            StatusText.Text = $"“{savedName}”已保存。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"保存失败：{exception.Message}";
        }
    }

    private void OnDeleteProviderClick(object sender, RoutedEventArgs e)
    {
        if (ProviderOrderBox.SelectedItem is not ListBoxItem selected) return;
        if (ProviderOrderBox.Items.Count == 1)
        {
            StatusText.Text = "至少保留一个翻译 API。";
            return;
        }
        var index = ProviderOrderBox.SelectedIndex;
        _editingProviderItem = null;
        ProviderOrderBox.Items.Remove(selected);
        UpdateProviderCount();
        ProviderOrderBox.SelectedIndex = Math.Min(index, ProviderOrderBox.Items.Count - 1);
    }

    private void UpdateProviderCount()
    {
        if (ProviderListTitle is not null) ProviderListTitle.Text = $"已添加接口（{ProviderOrderBox.Items.Count}）";
    }

    private void OnProviderOrderMouseDown(object sender, MouseButtonEventArgs e)
    {
        _providerDragStart = e.GetPosition(ProviderOrderBox);
        _draggedProviderItem = ItemsControl.ContainerFromElement(ProviderOrderBox, e.OriginalSource as DependencyObject) as ListBoxItem;
    }

    private void OnProviderOrderMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedProviderItem is null) return;
        var position = e.GetPosition(ProviderOrderBox);
        if (Math.Abs(position.X - _providerDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _providerDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ProviderOrderBox, _draggedProviderItem, DragDropEffects.Move);
    }

    private void OnProviderOrderDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ListBoxItem)) || e.Data.GetData(typeof(ListBoxItem)) is not ListBoxItem source) return;
        var target = ItemsControl.ContainerFromElement(ProviderOrderBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        var oldIndex = ProviderOrderBox.Items.IndexOf(source);
        var newIndex = target is null ? ProviderOrderBox.Items.Count - 1 : ProviderOrderBox.Items.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
        ProviderOrderBox.Items.RemoveAt(oldIndex);
        ProviderOrderBox.Items.Insert(newIndex, source);
        ProviderOrderBox.SelectedItem = source;
    }

    private static string DefaultEndpoint(TranslationProviderKind kind) => kind switch
    {
        TranslationProviderKind.MicrosoftTranslator => "https://api.cognitive.microsofttranslator.com",
        TranslationProviderKind.LibreTranslate => "https://libretranslate.com",
        TranslationProviderKind.MyMemory => "https://api.mymemory.translated.net/get",
        _ => string.Empty
    };

    private void OnProxyModeChanged(object sender, SelectionChangedEventArgs e) => UpdateCustomProxyFields();

    private void UpdateCustomProxyFields()
    {
        if (ProxySchemeBox is null) return;
        var enabled = ProxyModeBox.SelectedIndex == (int)ProxyMode.Custom;
        ProxySchemeBox.IsEnabled = ProxyHostBox.IsEnabled = ProxyPortBox.IsEnabled = ProxyUsernameBox.IsEnabled = ProxyPasswordBox.IsEnabled = enabled;
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadSettings();
            _store.Save(settings);
            TestButton.IsEnabled = false;
            StatusText.Text = "正在测试翻译服务…";
            if (_editingProviderItem?.Tag is not TranslationProviderSettings selected)
                throw new InvalidOperationException("请先选择要测试的翻译接口。");
            var targetIsEnglish = settings.TargetLanguage == "en";
            using var service = new TranslationService(_store);
            var result = await service.TestProviderAsync(selected, targetIsEnglish ? "你好" : "hello",
                targetIsEnglish ? "zh-CN" : "en", settings.TargetLanguage, CancellationToken.None);
            StatusText.Text = $"连接成功 · {result.ProviderName} · {result.ElapsedMilliseconds} ms · {result.Text}";
        }
        catch (Exception exception) { StatusText.Text = $"连接失败：{exception.Message}"; }
        finally { TestButton.IsEnabled = true; }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(StartupBox.IsChecked == true);
            _store.Save(ReadSettings());
            DialogResult = true;
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnCaptureHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (TryCaptureHotkey(e, out var modifiers, out var virtualKey))
        {
            _hotkeys = _hotkeys with { CaptureModifiers = modifiers, CaptureVirtualKey = virtualKey };
            CaptureHotkeyBox.Text = FormatHotkey(modifiers, virtualKey);
        }
    }

    private void OnTranslateHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (TryCaptureHotkey(e, out var modifiers, out var virtualKey))
        {
            _hotkeys = _hotkeys with { TranslateModifiers = modifiers, TranslateVirtualKey = virtualKey };
            TranslateHotkeyBox.Text = FormatHotkey(modifiers, virtualKey);
        }
    }

    private static bool TryCaptureHotkey(KeyEventArgs e, out uint modifiers, out uint virtualKey)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        modifiers = ToNativeModifiers(Keyboard.Modifiers);
        virtualKey = 0;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return false;
        if (modifiers == 0 && key is < Key.F1 or > Key.F12)
        {
            System.Media.SystemSounds.Beep.Play();
            return false;
        }
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= 0x0008;
        return result;
    }

    private static string FormatHotkey(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}
