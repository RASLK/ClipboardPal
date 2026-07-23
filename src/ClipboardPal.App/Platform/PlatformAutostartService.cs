using ClipboardPal.Core.Abstractions;
using Microsoft.Win32;

namespace ClipboardPal.Platform;

public sealed class PlatformAutostartService : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipboardPal";

    public bool IsEnabled
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }

            if (OperatingSystem.IsLinux())
            {
                var path = LinuxDesktopPath();
                return File.Exists(path);
            }

            if (OperatingSystem.IsMacOS())
            {
                // LaunchAgents plist presence
                return File.Exists(MacPlistPath());
            }

            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is not null)
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var path = LinuxDesktopPath();
            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var exe = Environment.ProcessPath ?? "ClipboardPal";
                File.WriteAllText(path,
                    $"[Desktop Entry]\nType=Application\nName=ClipboardPal\nExec=\"{exe}\"\nX-GNOME-Autostart-enabled=true\n");
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var path = MacPlistPath();
            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var exe = Environment.ProcessPath ?? "ClipboardPal";
                File.WriteAllText(path,
                    $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                      <key>Label</key><string>com.clipboardpal.app</string>
                      <key>ProgramArguments</key>
                      <array><string>{exe}</string></array>
                      <key>RunAtLoad</key><true/>
                    </dict>
                    </plist>
                    """);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string LinuxDesktopPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart", "clipboardpal.desktop");

    private static string MacPlistPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", "com.clipboardpal.app.plist");
}
