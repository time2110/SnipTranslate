using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using SnipTranslate.Native;

namespace SnipTranslate.Capture;

internal sealed class WindowTargetService
{
    private const int DwmwaCloaked = 14;
    private readonly IReadOnlyList<WindowTarget> _windows;
    private readonly ConcurrentDictionary<nint, IReadOnlyList<Rectangle>> _controls = new();
    private readonly ConcurrentDictionary<nint, byte> _loading = new();

    private WindowTargetService(IReadOnlyList<WindowTarget> windows) => _windows = windows;

    internal static WindowTargetService Snapshot()
    {
        var windows = new List<WindowTarget>();
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
            if (rectangle.Width >= 80 && rectangle.Height >= 50) windows.Add(new WindowTarget(handle, rectangle));
            return true;
        }, nint.Zero);
        return new WindowTargetService(windows);
    }

    internal WindowTarget? WindowAt(Point point) =>
        _windows.FirstOrDefault(window => window.Bounds.Contains(point));

    internal bool TryGetControls(nint handle, out IReadOnlyList<Rectangle> controls) =>
        _controls.TryGetValue(handle, out controls!);

    internal void WarmControlsAt(Point point, Action completed)
    {
        var window = WindowAt(point);
        if (window is null || _controls.ContainsKey(window.Handle) || !_loading.TryAdd(window.Handle, 0)) return;
        _ = Task.Run(() => CaptureControls(window))
            .ContinueWith(_ => completed(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private IReadOnlyList<Rectangle> CaptureControls(WindowTarget window)
    {
        try
        {
            var root = AutomationElement.FromHandle(window.Handle);
            var descendants = root.FindAll(TreeScope.Descendants, Automation.ControlViewCondition);
            var unique = new HashSet<Rectangle>();
            var maximum = Math.Min(descendants.Count, 1600);
            for (var index = 0; index < maximum; index++)
            {
                try
                {
                    var bounds = descendants[index].Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width < 6 || bounds.Height < 6) continue;
                    var rectangle = Rectangle.FromLTRB(
                        (int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
                        (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
                    rectangle = Rectangle.Intersect(rectangle, window.Bounds);
                    if (rectangle.Width < 6 || rectangle.Height < 6) continue;
                    if ((long)rectangle.Width * rectangle.Height >= (long)window.Bounds.Width * window.Bounds.Height * 95 / 100)
                        continue;
                    unique.Add(rectangle);
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            var result = unique.OrderBy(rectangle => (long)rectangle.Width * rectangle.Height).ToArray();
            _controls[window.Handle] = result;
            return result;
        }
        catch (Exception)
        {
            _controls[window.Handle] = [];
            return [];
        }
        finally
        {
            _loading.TryRemove(window.Handle, out _);
        }
    }
}

internal sealed record WindowTarget(nint Handle, Rectangle Bounds);
