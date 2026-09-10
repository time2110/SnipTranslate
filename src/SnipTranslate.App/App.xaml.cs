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
        _capture = new CaptureCoordinator(_ocr, _translator);
        _hotkeys = new HotkeyService();
        _hotkeys.CaptureRequested += (_, mode) => Dispatcher.Invoke(() => _capture.Begin(mode));

        _tray = new TrayService(
            capture: () => _capture.Begin(CaptureMode.Screenshot),
            translate: () => _capture.Begin(CaptureMode.Translate),
            settings: ShowSettings,
            exit: Shutdown);

        if (!_hotkeys.RegisterDefaults())
        {
            _tray.ShowMessage("快捷键冲突", "F1 或 Ctrl+F1 已被其他程序占用，可从托盘菜单启动截图。");
        }

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
        window.Show();
        window.Activate();
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
