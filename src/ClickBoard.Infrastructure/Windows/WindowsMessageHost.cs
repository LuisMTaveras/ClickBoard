using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClickBoard.Infrastructure.Windows;

public enum QuickMode
{
    KeyboardPicker,
    DockedPanel
}

public sealed class QuickModeEventArgs(QuickMode mode) : EventArgs
{
    public QuickMode Mode { get; } = mode;
}

public sealed class WindowsMessageHost : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int WmClipboardUpdate = 0x031D;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModNoRepeat = 0x4000;
    private const int HotkeyKeyboardPicker = 1;
    private const int HotkeyDockedPanel = 2;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly HotkeyBinding DefaultKeyboardBinding = ParseKnown("Ctrl+Shift+Space");
    private static readonly HotkeyBinding DefaultDockedBinding = ParseKnown("Ctrl+Alt+Space");

    private readonly HwndSource _source;
    private readonly bool _clipboardListenerRegistered;
    private HotkeyBinding? _keyboardBinding;
    private HotkeyBinding? _dockedBinding;
    private bool _keyboardHotkeyRegistered;
    private bool _dockedHotkeyRegistered;
    private bool _disposed;

    public event EventHandler? ClipboardChanged;
    public event EventHandler<QuickModeEventArgs>? QuickModeRequested;
    public event Action<int>? QueueActionRequested;
    public event Action<long>? FavoriteRequested;
    private readonly Dictionary<int, (long ClipId, HotkeyBinding Binding)> _favoriteHotkeys = [];
    private int _nextFavoriteId = 100;
    public bool TrySetFavoriteHotkey(long clipId, string? hotkey, out string error)
    {
        error = string.Empty;
        var previous = _favoriteHotkeys.FirstOrDefault(pair => pair.Value.ClipId == clipId);
        if (string.IsNullOrWhiteSpace(hotkey))
        { if (previous.Key != 0) { UnregisterHotKey(_source.Handle,previous.Key); _favoriteHotkeys.Remove(previous.Key); } return true; }
        if (!TryParseHotkey(hotkey,out var binding,out error)) return false;
        if (previous.Key != 0 && previous.Value.Binding == binding) return true;
        if (_favoriteHotkeys.Count >= 100 && previous.Key == 0) { error = "Máximo 100 atajos de favoritos."; return false; }
        var id = _nextFavoriteId++;
        if (!RegisterBinding(id,binding)) { error = $"El atajo {binding.Label} está ocupado. Se conserva el anterior."; return false; }
        if (previous.Key != 0) { UnregisterHotKey(_source.Handle,previous.Key); _favoriteHotkeys.Remove(previous.Key); }
        _favoriteHotkeys[id] = (clipId,binding); return true;
    }
    private readonly List<int> _queueHotkeys = [];
    public bool TryEnableQueue(out string error)
    {
        DisableQueue();
        var bindings = new[] { "Ctrl+Alt+Shift+V", "Ctrl+Alt+Shift+N", "Ctrl+Alt+Shift+B", "Ctrl+Alt+Shift+R" };
        for (var i = 0; i < bindings.Length; i++)
        {
            var id = 20 + i;
            if (!RegisterBinding(id, ParseKnown(bindings[i])))
            { error = $"El atajo {bindings[i]} está ocupado. No se activó la cola."; DisableQueue(); return false; }
            _queueHotkeys.Add(id);
        }
        error = string.Empty; return true;
    }
    public void DisableQueue()
    {
        foreach (var id in _queueHotkeys) UnregisterHotKey(_source.Handle, id);
        _queueHotkeys.Clear();
    }

    public string KeyboardPickerHotkey => _keyboardBinding?.Label ?? "Sin atajo";
    public string DockedPanelHotkey => _dockedBinding?.Label ?? "Sin atajo";
    public bool IsKeyboardPickerHotkeyRegistered => _keyboardHotkeyRegistered;
    public bool IsDockedPanelHotkeyRegistered => _dockedHotkeyRegistered;
    public string HotkeyStatus => string.Join(" · ", new[]
    {
        _keyboardHotkeyRegistered ? $"Selector: {KeyboardPickerHotkey}" : $"Selector: {KeyboardPickerHotkey} (ocupado)",
        _dockedHotkeyRegistered ? $"Panel: {DockedPanelHotkey}" : $"Panel: {DockedPanelHotkey} (ocupado)"
    });

    public WindowsMessageHost(string keyboardPickerHotkey, string dockedPanelHotkey)
    {
        var parameters = new HwndSourceParameters("ClickBoard background listener")
        {
            ParentWindow = HwndMessage,
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WindowProc);
        _clipboardListenerRegistered = AddClipboardFormatListener(_source.Handle);

        if (!TrySetHotkeys(keyboardPickerHotkey, dockedPanelHotkey, out _))
            TrySetHotkeys(DefaultKeyboardBinding.Label, DefaultDockedBinding.Label, out _);
    }

    public bool TrySetHotkeys(string keyboardPickerHotkey, string dockedPanelHotkey, out string error)
    {
        error = string.Empty;
        if (!TryParseHotkey(keyboardPickerHotkey, out var keyboardBinding, out error) ||
            !TryParseHotkey(dockedPanelHotkey, out var dockedBinding, out error))
            return false;

        if (keyboardBinding.VirtualKey == dockedBinding.VirtualKey &&
            keyboardBinding.Modifiers == dockedBinding.Modifiers)
        {
            error = "El selector y el panel necesitan atajos distintos.";
            return false;
        }

        var previousKeyboardBinding = _keyboardBinding;
        var previousDockedBinding = _dockedBinding;
        UnregisterCurrentHotkeys();

        var keyboardRegistered = RegisterBinding(HotkeyKeyboardPicker, keyboardBinding);
        var dockedRegistered = RegisterBinding(HotkeyDockedPanel, dockedBinding);
        if (keyboardRegistered && dockedRegistered)
        {
            _keyboardBinding = keyboardBinding;
            _dockedBinding = dockedBinding;
            _keyboardHotkeyRegistered = true;
            _dockedHotkeyRegistered = true;
            return true;
        }

        if (keyboardRegistered)
            UnregisterHotKey(_source.Handle, HotkeyKeyboardPicker);
        if (dockedRegistered)
            UnregisterHotKey(_source.Handle, HotkeyDockedPanel);

        _keyboardBinding = previousKeyboardBinding;
        _dockedBinding = previousDockedBinding;
        _keyboardHotkeyRegistered = previousKeyboardBinding is { } oldKeyboardBinding && RegisterBinding(HotkeyKeyboardPicker, oldKeyboardBinding);
        _dockedHotkeyRegistered = previousDockedBinding is { } oldDockedBinding && RegisterBinding(HotkeyDockedPanel, oldDockedBinding);
        error = "Windows no pudo registrar uno de esos atajos. Prueba otra combinación.";
        return false;
    }

    private static bool TryParseHotkey(string? value, out HotkeyBinding binding, out string error)
    {
        binding = default;
        error = string.Empty;
        var parts = (value ?? string.Empty).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = 0;
        Key? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                case "mayús":
                case "mayus":
                case "mayúsculas":
                case "mayusculas":
                    modifiers |= ModShift;
                    break;
                default:
                    if (key is not null || !TryParseKey(part, out var parsedKey))
                    {
                        error = $"No se reconoce el atajo «{value}».";
                        return false;
                    }
                    key = parsedKey;
                    break;
            }
        }

        if (key is null || modifiers == 0)
        {
            error = "Cada atajo debe incluir Ctrl, Alt o Shift y una tecla.";
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key.Value);
        if (virtualKey <= 0)
        {
            error = "Esa tecla no se puede usar como atajo global.";
            return false;
        }

        binding = new HotkeyBinding(virtualKey, modifiers, FormatLabel(modifiers, key.Value));
        return true;
    }

    private static bool TryParseKey(string value, out Key key)
    {
        key = value switch
        {
            "-" => Key.OemMinus,
            "=" => Key.OemPlus,
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "/" => Key.OemQuestion,
            _ => Key.None
        };
        if (key != Key.None)
            return true;
        if (value.Equals("Spacebar", StringComparison.OrdinalIgnoreCase))
            value = "Space";
        if (value.Length == 1 && char.IsDigit(value[0]))
            value = $"D{value}";
        return Enum.TryParse(value, ignoreCase: true, out key) && key is not Key.None and not Key.System;
    }

    private static string FormatLabel(int modifiers, Key key)
    {
        var parts = new List<string>(4);
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            Key.Space => "Space",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString()
        });
        return string.Join('+', parts);
    }

    private static HotkeyBinding ParseKnown(string value)
    {
        TryParseHotkey(value, out var binding, out _);
        return binding;
    }

    private bool RegisterBinding(int id, HotkeyBinding binding)
    {
        bool Same(HotkeyBinding other) => other.Modifiers == binding.Modifiers && other.VirtualKey == binding.VirtualKey;
        if (id != HotkeyKeyboardPicker && _keyboardHotkeyRegistered && _keyboardBinding is { } keyboard && Same(keyboard)) return false;
        if (id != HotkeyDockedPanel && _dockedHotkeyRegistered && _dockedBinding is { } docked && Same(docked)) return false;
        if (_favoriteHotkeys.Any(pair => pair.Key != id && Same(pair.Value.Binding))) return false;
        var queue = new[] { "Ctrl+Alt+Shift+V", "Ctrl+Alt+Shift+N", "Ctrl+Alt+Shift+B", "Ctrl+Alt+Shift+R" };
        if (_queueHotkeys.Any(key => key != id && Same(ParseKnown(queue[key - 20])))) return false;
        return RegisterHotKey(_source.Handle, id, binding.Modifiers | ModNoRepeat, binding.VirtualKey);
    }

    private void UnregisterCurrentHotkeys()
    {
        if (_keyboardHotkeyRegistered)
            UnregisterHotKey(_source.Handle, HotkeyKeyboardPicker);
        if (_dockedHotkeyRegistered)
            UnregisterHotKey(_source.Handle, HotkeyDockedPanel);
        _keyboardHotkeyRegistered = false;
        _dockedHotkeyRegistered = false;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate)
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (message == WmHotKey)
        {
            var hotkeyId = wParam.ToInt32();
            if (_favoriteHotkeys.TryGetValue(hotkeyId,out var favorite)) FavoriteRequested?.Invoke(favorite.ClipId);
            if (_queueHotkeys.Contains(hotkeyId)) QueueActionRequested?.Invoke(hotkeyId - 20);
            if (hotkeyId == HotkeyKeyboardPicker)
                QuickModeRequested?.Invoke(this, new QuickModeEventArgs(QuickMode.KeyboardPicker));
            else if (hotkeyId == HotkeyDockedPanel)
                QuickModeRequested?.Invoke(this, new QuickModeEventArgs(QuickMode.DockedPanel));
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisableQueue();
        foreach (var id in _favoriteHotkeys.Keys) UnregisterHotKey(_source.Handle,id);
        _favoriteHotkeys.Clear();

        if (_clipboardListenerRegistered)
            RemoveClipboardFormatListener(_source.Handle);
        UnregisterCurrentHotkeys();
        _source.RemoveHook(WindowProc);
        _source.Dispose();
    }

    private readonly record struct HotkeyBinding(int VirtualKey, int Modifiers, string Label);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
