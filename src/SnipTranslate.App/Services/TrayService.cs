using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace SnipTranslate.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;

    internal TrayService(Action capture, Action translate, Action settings, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("截图    F1", null, (_, _) => capture());
        menu.Items.Add("截图翻译    Ctrl+F1", null, (_, _) => translate());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        _trayIcon = CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "SnipTranslate · 双击截图",
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => capture();
    }

    internal void ShowMessage(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(4000, title, message, Forms.ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var background = new SolidBrush(Color.FromArgb(14, 165, 233));
        using var shape = RoundedRectangle(new RectangleF(1, 1, 30, 30), 7);
        graphics.FillPath(background, shape);

        using var cropPen = new Pen(Color.FromArgb(220, 255, 255, 255), 2.4f)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };
        graphics.DrawLines(cropPen, [new PointF(5, 12), new PointF(5, 5), new PointF(12, 5)]);
        graphics.DrawLines(cropPen, [new PointF(20, 27), new PointF(27, 27), new PointF(27, 20)]);

        using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("T", font, textBrush, new RectangleF(5, 5, 22, 23), format);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
