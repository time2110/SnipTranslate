using System.Windows.Interop;
using SnipTranslate.Capture;
using SnipTranslate.Native;

namespace SnipTranslate.Services;

internal sealed class HotkeyService : IDisposable
{
    private const int CaptureHotkeyId = 0x5101;
    private const int TranslateHotkeyId = 0x5102;
    private readonly HwndSource _source;
    private bool _captureRegistered;
    private bool _translateRegistered;

    internal event EventHandler<CaptureMode>? CaptureRequested;

    internal HotkeyService()
    {
        var parameters = new HwndSourceParameters("SnipTranslate.Hotkeys")
        {
            ParentWindow = new nint(-3),
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    internal bool RegisterDefaults()
    {
        _captureRegistered = NativeMethods.RegisterHotKey(
            _source.Handle,
            CaptureHotkeyId,
            0,
            NativeMethods.VkF1);

        _translateRegistered = NativeMethods.RegisterHotKey(
            _source.Handle,
            TranslateHotkeyId,
            NativeMethods.ModControl,
            NativeMethods.VkF1);

        return _captureRegistered && _translateRegistered;
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey)
        {
            return nint.Zero;
        }

        var id = wParam.ToInt32();
        if (id == CaptureHotkeyId)
        {
            CaptureRequested?.Invoke(this, CaptureMode.Screenshot);
            handled = true;
        }
        else if (id == TranslateHotkeyId)
        {
            CaptureRequested?.Invoke(this, CaptureMode.Translate);
            handled = true;
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_captureRegistered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, CaptureHotkeyId);
        }

        if (_translateRegistered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, TranslateHotkeyId);
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }
}

