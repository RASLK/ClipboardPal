using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipboardPal.Core.Abstractions;

namespace ClipboardPal.Platform;

public sealed class PlatformPasteService : IPasteService
{
    public nint CaptureForegroundWindow()
    {
        if (OperatingSystem.IsWindows())
            return WindowsPaste.GetForegroundWindow();
        return 0;
    }

    public async Task PasteToAsync(nint targetWindow, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            await WindowsPaste.PasteAsync(targetWindow, cancellationToken).ConfigureAwait(false);
            return;
        }

        // macOS / Linux: clipboard is already set; user typically pastes with Cmd/Ctrl+V.
        // Best-effort: simulate via SharpHook EventSimulator when available.
        await Task.Delay(80, cancellationToken).ConfigureAwait(false);
        try
        {
            var sim = new SharpHook.EventSimulator();
            if (OperatingSystem.IsMacOS())
            {
                sim.SimulateKeyPress(SharpHook.Native.KeyCode.VcLeftMeta);
                sim.SimulateKeyPress(SharpHook.Native.KeyCode.VcV);
                sim.SimulateKeyRelease(SharpHook.Native.KeyCode.VcV);
                sim.SimulateKeyRelease(SharpHook.Native.KeyCode.VcLeftMeta);
            }
            else
            {
                sim.SimulateKeyPress(SharpHook.Native.KeyCode.VcLeftControl);
                sim.SimulateKeyPress(SharpHook.Native.KeyCode.VcV);
                sim.SimulateKeyRelease(SharpHook.Native.KeyCode.VcV);
                sim.SimulateKeyRelease(SharpHook.Native.KeyCode.VcLeftControl);
            }
        }
        catch
        {
            // User can still paste manually.
        }
    }
}

[SupportedOSPlatform("windows")]
file static class WindowsPaste
{
    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    public static async Task PasteAsync(nint targetWindow, CancellationToken cancellationToken)
    {
        if (targetWindow != 0)
        {
            SetForegroundWindow(targetWindow);
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        var inputs = new[]
        {
            Key(VK_CONTROL, up: false),
            Key(VK_V, up: false),
            Key(VK_V, up: true),
            Key(VK_CONTROL, up: true)
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? KEYEVENTF_KEYUP : 0
            }
        }
    };
}
