using System.Threading;
using System.Windows;
using SnipTranslate.Capture;
using SnipTranslate.Ocr;
using SnipTranslate.Services;
using SnipTranslate.Settings;
using SnipTranslate.Translation;

namespace SnipTranslate;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private HotkeyService? _hotkeys;
    private TrayService? _tray;
    private CaptureCoordinator? _capture;
    private OcrClient? _ocr;
    private AppSettingsStore? _settings;
    private TranslationService? _translator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "SnipTranslate.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _ocr = new OcrClient();
        _settings = new AppSettingsStore();
        _translator = new TranslationService(_settings);
        _capture = new CaptureCoordinator(_ocr, _translator, _settings);
        _hotkeys = new HotkeyService();
        _hotkeys.CaptureRequested += (_, mode) => Dispatcher.Invoke(() => _capture.Begin(mode));

        _tray = new TrayService(
            capture: () => _capture.Begin(CaptureMode.Screenshot),
            translate: () => _capture.Begin(CaptureMode.Translate),
            settings: ShowSettings,
            exit: Shutdown);

        RegisterHotkeys(showSuccess: false);

        if (e.Args.Contains("--capture", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => _capture.Begin(CaptureMode.Screenshot));
        }
    }

    private void ShowSettings()
    {
        var existing = Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        var window = new SettingsWindow(_settings!);
        window.Closed += (_, _) => RegisterHotkeys(showSuccess: false);
        window.Show();
        window.Activate();
    }

    private void RegisterHotkeys(bool showSuccess)
    {
        if (_hotkeys is null || _settings is null || _tray is null) return;
        var result = _hotkeys.Register(_settings.Current.Hotkeys);
        if (!result.Success)
        {
            var conflicts = new List<string>();
            if (!result.CaptureRegistered) conflicts.Add("截图");
            if (!result.TranslateRegistered) conflicts.Add("截图翻译");
            _tray.ShowMessage("快捷键冲突", $"{string.Join("、", conflicts)}快捷键已被占用，请在设置中修改。");
        }
        else if (showSuccess)
        {
            _tray.ShowMessage("快捷键已更新", "新的全局快捷键已经生效。");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _capture?.Dispose();
        _translator?.Dispose();
        _ocr?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
