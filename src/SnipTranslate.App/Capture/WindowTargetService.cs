using System.Diagnostics;
using System.Drawing;
using System.Text;
using SnipTranslate.Native;

namespace SnipTranslate.Capture;

internal static class WindowTargetService
{
    private const int DwmwaCloaked = 14;

    internal static IReadOnlyList<Rectangle> Snapshot()
    {
        var windows = new List<Rectangle>();
        var ownProcessId = (uint)Environment.ProcessId;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle)) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == ownProcessId) return true;
            if (NativeMethods.DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var className = new StringBuilder(128);
            NativeMethods.GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;
            if (!NativeMethods.GetWindowRect(handle, out var native)) return true;
            var rectangle = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            if (rectangle.Width >= 80 && rectangle.Height >= 50) windows.Add(rectangle);
            return true;
        }, nint.Zero);
        return windows;
    }
}
