using System.IO;
using Microsoft.Win32;

namespace SnipTranslate.Settings;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnipTranslate";

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command))
        {
            return true;
        }
        return File.Exists(LegacyStartupShortcutPath());
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 当前用户启动项。");
        if (enabled)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                throw new InvalidOperationException("无法确定 SnipTranslate 程序路径。");
            }
            key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        // 兼容早期安装包创建的启动文件夹快捷方式，防止产生两个启动入口。
        var legacyShortcut = LegacyStartupShortcutPath();
        if (File.Exists(legacyShortcut)) File.Delete(legacyShortcut);
    }

    private static string LegacyStartupShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "SnipTranslate.lnk");
}
