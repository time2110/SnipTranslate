using System.Drawing;
using Forms = System.Windows.Forms;

namespace SnipTranslate.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    internal TrayService(Action capture, Action translate, Action settings, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("截图    F1", null, (_, _) => capture());
        menu.Items.Add("截图翻译    Ctrl+F1", null, (_, _) => translate());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "SnipTranslate",
            Icon = SystemIcons.Application,
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
    }
}
