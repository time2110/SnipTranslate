using System.Windows;
using SnipTranslate.Diagnostics;
using SnipTranslate.Ocr;
using SnipTranslate.Translation;

namespace SnipTranslate.Capture;

internal sealed class CaptureCoordinator : IDisposable
{
    private readonly List<CaptureWindow> _windows = [];
    private readonly OcrClient _ocr;
    private readonly ITranslationProvider _translator;
    private bool _capturing;

    internal CaptureCoordinator(OcrClient ocr, ITranslationProvider translator)
    {
        _ocr = ocr;
        _translator = translator;
    }

    internal void Begin(CaptureMode mode)
    {
        if (_capturing)
        {
            CloseAll();
            return;
        }

        _capturing = true;
        var stage = "初始化";
        try
        {
            AppLog.Info($"Capture requested. Mode={mode}");
            if (mode == CaptureMode.Translate)
            {
                stage = "启动 OCR";
                _ocr.Prepare();
            }

            stage = "读取屏幕像素";
            var frames = ScreenCaptureService.CaptureAllDisplays();
            AppLog.Info($"Captured {frames.Count} display(s).");
            stage = "创建截图遮罩";
            foreach (var frame in frames)
            {
                var window = new CaptureWindow(frame, mode, _ocr, _translator, CloseAll);
                _windows.Add(window);
                window.Show();
            }

            AppLog.Info("Capture overlays are visible.");
        }
        catch (Exception exception)
        {
            AppLog.Error($"Capture failed during: {stage}", exception);
            CloseAll();
            MessageBox.Show(
                $"无法截取屏幕。\n\n阶段：{stage}\n{exception.GetType().Name}: {exception.Message}\n\n日志：{AppLog.FilePath}",
                "SnipTranslate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    internal void CloseAll()
    {
        var windows = _windows.ToArray();
        _windows.Clear();
        _capturing = false;

        foreach (var window in windows)
        {
            window.Close();
        }
    }

    public void Dispose() => CloseAll();
}
