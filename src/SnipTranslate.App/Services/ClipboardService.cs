using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnipTranslate.Services;

internal static class ClipboardService
{
    internal static void SetText(string text) => Retry(() => Clipboard.SetText(text));

    internal static void SetImage(BitmapSource image) => Retry(() => Clipboard.SetImage(image));

    private static void Retry(Action action)
    {
        COMException? lastError = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException exception)
            {
                lastError = exception;
                Thread.Sleep(20 * (attempt + 1));
            }
        }

        throw new InvalidOperationException("剪贴板正被其他程序占用，请稍后重试。", lastError);
    }
}
