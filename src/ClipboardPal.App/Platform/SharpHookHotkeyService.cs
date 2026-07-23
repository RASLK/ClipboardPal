using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using SharpHook;
using SharpHook.Native;

namespace ClipboardPal.Platform;

/// <summary>
/// Cross-platform global hotkeys + pointer tracking via SharpHook (libuiohook).
/// </summary>
public sealed class SharpHookHotkeyService : IGlobalHotkeyService, IPointerTracker
{
    private readonly AppSettings _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly TaskPoolGlobalHook _hook;
    private readonly HashSet<KeyCode> _down = [];

    private bool _chordActive;
    private bool _quickModeSession;
    private Action<HotkeySpec?>? _capture;
    private Task? _runTask;
    private int _pointerX;
    private int _pointerY;
    private int _hasPointer;

    public bool IsInstalled { get; private set; }
    public bool PanelVisible { get; set; }

    public event Action? HotkeyPressed;
    public event Action<int>? QuickSlotPressed;
    public event Action<bool>? QuickChordChanged;
    public event Action? QuickModeCycle;
    public event Action? QuickModeCommit;
    public event Action<int, int>? MousePressed;

    public SharpHookHotkeyService(AppSettings settings, IUiDispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.MouseMoved += OnMouseMoved;
        _hook.MouseDragged += OnMouseMoved;
        _hook.MousePressed += OnMousePressed;

        try
        {
            _runTask = _hook.RunAsync();
            IsInstalled = true;
        }
        catch
        {
            IsInstalled = false;
        }
    }

    public void BeginCapture(Action<HotkeySpec?> onCaptured) => _capture = onCaptured;
    public void EndCapture() => _capture = null;
    public void RecheckChord() => UpdateChord();

    public bool TryGetPosition(out int x, out int y)
    {
        if (Volatile.Read(ref _hasPointer) != 1)
        {
            x = y = 0;
            return false;
        }

        x = Volatile.Read(ref _pointerX);
        y = Volatile.Read(ref _pointerY);
        return true;
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        Volatile.Write(ref _pointerX, e.Data.X);
        Volatile.Write(ref _pointerY, e.Data.Y);
        Volatile.Write(ref _hasPointer, 1);
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (e.IsEventSimulated)
            return;
        if (e.Data.Button is not SharpHook.Native.MouseButton.Button1)
            return;

        Volatile.Write(ref _pointerX, e.Data.X);
        Volatile.Write(ref _pointerY, e.Data.Y);
        Volatile.Write(ref _hasPointer, 1);
        MousePressed?.Invoke(e.Data.X, e.Data.Y);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated)
            return;

        var key = e.Data.KeyCode;
        _down.Add(key);

        if (_capture is not null)
        {
            HandleCapture(key, e);
            return;
        }

        UpdateChord();

        var hk = _settings.Hotkey;
        if (!ModifiersMatch(hk))
            return;

        if (key == ToKeyCode(hk.KeyName))
        {
            e.SuppressEvent = true;
            NeutralizeMetaIfNeeded();

            // Quick mode: panel already open + modifiers held → cycle clips instead of toggle.
            if (PanelVisible && _settings.QuickModeEnabled)
            {
                _quickModeSession = true;
                _dispatcher.Post(() => QuickModeCycle?.Invoke());
                return;
            }

            _dispatcher.Post(() => HotkeyPressed?.Invoke());
            return;
        }

        if (PanelVisible && _settings.QuickSlotsEnabled && TryDigitSlot(key, out var slot))
        {
            e.SuppressEvent = true;
            NeutralizeMetaIfNeeded();
            _dispatcher.Post(() => QuickSlotPressed?.Invoke(slot));
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated)
            return;

        _down.Remove(e.Data.KeyCode);

        var wasChord = _chordActive;
        UpdateChord();

        // Quick mode commit: after cycling, release all hotkey modifiers → paste/copy.
        if (_quickModeSession && PanelVisible && _settings.QuickModeEnabled && wasChord && !_chordActive)
        {
            _quickModeSession = false;
            _dispatcher.Post(() => QuickModeCommit?.Invoke());
        }
    }

    private void HandleCapture(KeyCode key, KeyboardHookEventArgs e)
    {
        e.SuppressEvent = true;

        if (IsModifier(key))
            return;

        HotkeySpec? spec = null;
        if (key != KeyCode.VcEscape)
        {
            spec = new HotkeySpec
            {
                Meta = IsMetaDown(),
                Shift = IsDown(KeyCode.VcLeftShift) || IsDown(KeyCode.VcRightShift),
                Ctrl = IsDown(KeyCode.VcLeftControl) || IsDown(KeyCode.VcRightControl),
                Alt = IsDown(KeyCode.VcLeftAlt) || IsDown(KeyCode.VcRightAlt),
                KeyName = FromKeyCode(key)
            };
            if (!spec.HasModifier)
                return;
        }

        var callback = _capture;
        _capture = null;
        if (spec?.Meta == true)
            NeutralizeMetaIfNeeded();
        _dispatcher.Post(() => callback?.Invoke(spec));
    }

    private void UpdateChord()
    {
        var active = PanelVisible && _settings.QuickSlotsEnabled && ModifiersMatch(_settings.Hotkey);
        if (active == _chordActive)
            return;
        _chordActive = active;
        _dispatcher.Post(() => QuickChordChanged?.Invoke(active));
    }

    private bool ModifiersMatch(HotkeySpec hk) =>
        IsMetaDown() == hk.Meta &&
        (IsDown(KeyCode.VcLeftShift) || IsDown(KeyCode.VcRightShift)) == hk.Shift &&
        (IsDown(KeyCode.VcLeftControl) || IsDown(KeyCode.VcRightControl)) == hk.Ctrl &&
        (IsDown(KeyCode.VcLeftAlt) || IsDown(KeyCode.VcRightAlt)) == hk.Alt;

    private bool IsMetaDown() =>
        IsDown(KeyCode.VcLeftMeta) || IsDown(KeyCode.VcRightMeta);

    private bool IsDown(KeyCode key) => _down.Contains(key);

    private static bool IsModifier(KeyCode key) => key is
        KeyCode.VcLeftShift or KeyCode.VcRightShift or
        KeyCode.VcLeftControl or KeyCode.VcRightControl or
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta;

    private static bool TryDigitSlot(KeyCode key, out int slot)
    {
        slot = key switch
        {
            KeyCode.Vc1 => 1,
            KeyCode.Vc2 => 2,
            KeyCode.Vc3 => 3,
            KeyCode.Vc4 => 4,
            KeyCode.Vc5 => 5,
            KeyCode.Vc6 => 6,
            KeyCode.Vc7 => 7,
            KeyCode.Vc8 => 8,
            KeyCode.Vc9 => 9,
            KeyCode.Vc0 => 10,
            _ => 0
        };
        return slot != 0;
    }

    private static KeyCode ToKeyCode(string name)
    {
        if (name.Length == 1 && char.IsLetter(name[0]))
            return Enum.Parse<KeyCode>("Vc" + char.ToUpperInvariant(name[0]));

        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return Enum.Parse<KeyCode>("Vc" + name[1]);

        return name.ToUpperInvariant() switch
        {
            "V" => KeyCode.VcV,
            "ESCAPE" => KeyCode.VcEscape,
            _ => Enum.TryParse<KeyCode>("Vc" + name, true, out var k) ? k : KeyCode.VcV
        };
    }

    private static string FromKeyCode(KeyCode key)
    {
        var s = key.ToString();
        if (s.StartsWith("Vc", StringComparison.Ordinal) && s.Length == 3 && char.IsLetter(s[2]))
            return s[2].ToString();
        if (s.StartsWith("Vc", StringComparison.Ordinal) && s.Length == 3 && char.IsDigit(s[2]))
            return "D" + s[2];
        return s.StartsWith("Vc", StringComparison.Ordinal) ? s[2..] : s;
    }

    private void NeutralizeMetaIfNeeded()
    {
        if (!IsMetaDown()) return;
        try
        {
            var sim = new EventSimulator();
            sim.SimulateKeyPress(KeyCode.VcF24);
            sim.SimulateKeyRelease(KeyCode.VcF24);
        }
        catch
        {
            // ignore
        }
    }

    public async ValueTask DisposeAsync()
    {
        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.MouseMoved -= OnMouseMoved;
        _hook.MouseDragged -= OnMouseMoved;
        _hook.MousePressed -= OnMousePressed;
        _hook.Dispose();
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
    }
}
