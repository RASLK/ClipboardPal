using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipboardPal.Core.Abstractions;

namespace ClipboardPal.Platform;

public sealed class ProcessForegroundAppService : IForegroundAppService
{
    public string? GetForegroundProcessName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return WindowsForeground.GetProcessName();
            if (OperatingSystem.IsMacOS())
                return MacForeground.GetProcessName();
            if (OperatingSystem.IsLinux())
                return LinuxForeground.GetProcessName();
            return null;
        }
        catch
        {
            return null;
        }
    }
}

[SupportedOSPlatform("windows")]
file static class WindowsForeground
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    public static string? GetProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        using var process = Process.GetProcessById((int)pid);
        return process.ProcessName;
    }
}

file static class MacForeground
{
    public static string? GetProcessName()
    {
        // Frontmost app bundle id / name via AppleScript.
        var name = RunCapture(
            "osascript",
            "-e",
            "tell application \"System Events\" to get name of first application process whose frontmost is true");
        if (!string.IsNullOrWhiteSpace(name))
            return Sanitize(name);

        return null;
    }

    private static string? RunCapture(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(1500))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Sanitize(string value)
    {
        // "Google Chrome" → "Google Chrome" (exclusions compare lowercase)
        return value.Trim().Trim('"');
    }
}

file static class LinuxForeground
{
    public static string? GetProcessName()
    {
        // Prefer xdotool when available (X11).
        var pidText = RunCapture("xdotool", "getactivewindow", "getwindowpid");
        if (int.TryParse(pidText, out var pid) && pid > 0)
            return NameFromPid(pid);

        // Fallback: read _NET_ACTIVE_WINDOW via xprop.
        var xprop = RunCapture("xprop", "-root", "_NET_ACTIVE_WINDOW");
        if (!string.IsNullOrWhiteSpace(xprop))
        {
            var hex = xprop.Split(' ').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var pidProp = RunCapture("xprop", "-id", hex!, "_NET_WM_PID");
                var digits = new string((pidProp ?? "").Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out pid) && pid > 0)
                    return NameFromPid(pid);
            }
        }

        return null;
    }

    private static string? NameFromPid(int pid)
    {
        try
        {
            var comm = $"/proc/{pid}/comm";
            if (File.Exists(comm))
                return File.ReadAllText(comm).Trim();

            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? RunCapture(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(1500))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
