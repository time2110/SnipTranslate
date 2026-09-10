using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using SnipTranslate.Native;
using SnipTranslate.Diagnostics;

namespace SnipTranslate.Capture;

internal static class ScreenCaptureService
{
    internal static IReadOnlyList<CaptureFrame> CaptureAllDisplays()
    {
        var frames = new List<CaptureFrame>();

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            AppLog.Info($"Capturing display {screen.DeviceName}: {bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height}");
            try
            {
                frames.Add(new CaptureFrame(bounds, CaptureDisplay(bounds)));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"显示器 {screen.DeviceName} ({bounds.Left},{bounds.Top}, {bounds.Width}x{bounds.Height}) 捕获失败。",
                    exception);
            }
        }

        return frames;
    }

    private static BitmapSource CaptureDisplay(System.Drawing.Rectangle bounds)
    {
        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            ThrowLastWin32Error("无法取得屏幕设备上下文");
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousObject = nint.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                ThrowLastWin32Error("无法创建截图设备上下文");
            }

            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (bitmap == nint.Zero)
            {
                ThrowLastWin32Error("无法创建截图位图");
            }

            previousObject = NativeMethods.SelectObject(memoryDc, bitmap);
            if (previousObject == nint.Zero || previousObject == new nint(-1))
            {
                ThrowLastWin32Error("无法选择截图位图");
            }

            if (!NativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDc,
                    bounds.Left,
                    bounds.Top,
                    NativeMethods.Srccopy | NativeMethods.CaptureBlt))
            {
                ThrowLastWin32Error("无法复制屏幕像素");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        finally
        {
            if (previousObject != nint.Zero && memoryDc != nint.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousObject);
            }

            if (bitmap != nint.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static void ThrowLastWin32Error(string message)
    {
        throw new Win32Exception(Marshal.GetLastPInvokeError(), message);
    }
}
