using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClickBoard.Infrastructure.Windows;

public sealed class ForegroundWindowTracker : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private readonly WinEventDelegate _callback;
    private readonly IntPtr _hook;
    private IntPtr _lastExternalWindow;

    public IntPtr LastExternalWindow => Interlocked.CompareExchange(ref _lastExternalWindow, IntPtr.Zero, IntPtr.Zero);

    public static bool IsOwnProcessWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var id);
        return id == Environment.ProcessId;
    }

    public static bool IsClipboardOwnedByCurrentProcess() => IsOwnProcessWindow(GetClipboardOwner());

    public static string? GetProcessName(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var id);
        if (id == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static string? GetFriendlyApplicationName(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var id);
        if (id == 0 || id == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            try
            {
                var description = process.MainModule?.FileVersionInfo.FileDescription?.Trim();
                if (!string.IsNullOrWhiteSpace(description))
                    return description;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
            }
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public (string Process, string Title)? GetActiveContext()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out var id);
        if (id == 0 || id == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            var title = new StringBuilder(1024);
            GetWindowText(window, title, title.Capacity);
            return (process.ProcessName, title.ToString());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public ForegroundWindowTracker()
    {
        var current = GetForegroundWindow();
        _lastExternalWindow = IsOwnProcessWindow(current) ? IntPtr.Zero : current;
        _callback = OnForegroundChanged;
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
    }

    public string? GetForegroundProcessName()
    {
        var window = GetClipboardOwner();
        if (window == IntPtr.Zero || IsOwnProcessWindow(window))
            window = LastExternalWindow;
        return GetProcessName(window);
    }

    private void OnForegroundChanged(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (window != IntPtr.Zero && !IsOwnProcessWindow(window))
            Interlocked.Exchange(ref _lastExternalWindow, window);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
