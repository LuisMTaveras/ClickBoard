using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ClickBoard.Infrastructure.Storage;
using System.Windows;
using System.Windows.Media.Imaging;
using ClickBoard.Core.Models;

namespace ClickBoard.Infrastructure.Windows;

public sealed class WindowsPasteService(ForegroundWindowTracker foregroundWindowTracker)
{
    public const string InternalClipboardFormat = "ClickBoard.InternalClipboard";
    private readonly SemaphoreSlim _pasteGate = new(1, 1);
    public IntPtr CaptureTargetWindow() => foregroundWindowTracker.LastExternalWindow;
    public string DestinationLabel => ForegroundWindowTracker.GetProcessName(CaptureTargetWindow()) is { } name ? "Pegar en " + name : "Pegar clip";

    public async Task CopyAsync(ClipboardClip clip, CancellationToken cancellationToken = default)
    {
        if (!await _pasteGate.WaitAsync(0, cancellationToken)) throw new PasteBusyException();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { await Application.Current.Dispatcher.InvokeAsync(() => SetClipboardData(clip, null)); return; }
                catch (COMException) when (attempt < 7) { await Task.Delay(100 * (attempt + 1), cancellationToken); }
            }
        }
        finally { _pasteGate.Release(); }
    }

    public async Task<bool> PasteAsync(ClipboardClip clip, CancellationToken cancellationToken = default, IntPtr? targetWindow = null)
    {
        var target = targetWindow ?? CaptureTargetWindow();
        if (!await _pasteGate.WaitAsync(0, cancellationToken)) throw new PasteBusyException();
        try { return await PasteCoreAsync(clip, target, cancellationToken); }
        finally { _pasteGate.Release(); }
    }

    private async Task<bool> PasteCoreAsync(ClipboardClip clip, IntPtr targetWindow, CancellationToken cancellationToken)
    {
        if (clip.Kind == ClipKind.Text && PasteApplicationRules.UsePlainText(targetWindow))
            clip = clip with { HtmlContent = null, RtfContent = null, ImageBytes = null, FilesJson = null };
        var folderFile = IsFolderDestination(targetWindow)
            ? await CreateFolderPasteFileAsync(clip, cancellationToken)
            : null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() => SetClipboardData(clip, folderFile));
                break;
            }
            catch (COMException) when (attempt < 7)
            {
                await Task.Delay(100 * (attempt + 1), cancellationToken);
            }
        }

        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
            return false;

        for (var attempt = 0; ModifiersArePressed(); attempt++)
        {
            if (attempt >= 50) return false;
            await Task.Delay(20, cancellationToken);
        }
        await Task.Delay(50, cancellationToken);
        SetForegroundWindow(targetWindow);
        await Task.Delay(100, cancellationToken);
        if (GetForegroundWindow() != targetWindow)
            return false;
        return SendPasteShortcut();
    }

    private static void SetClipboardData(ClipboardClip clip, string? folderFile)
    {
        var data = new DataObject();
        data.SetData(InternalClipboardFormat, "1");
        if (clip.Kind == ClipKind.Files && string.IsNullOrEmpty(clip.FilesJson))
            throw new FileNotFoundException("No se encontró la lista de archivos originales.");
        if (!string.IsNullOrEmpty(clip.TextContent))
            data.SetText(clip.TextContent, TextDataFormat.UnicodeText);
        if (!string.IsNullOrEmpty(clip.HtmlContent))
            data.SetData(DataFormats.Html, clip.HtmlContent);
        if (!string.IsNullOrEmpty(clip.RtfContent))
            data.SetData(DataFormats.Rtf, clip.RtfContent);
        if (clip.ImageBytes is { Length: > 0 })
        {
            using var stream = new MemoryStream(clip.ImageBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            data.SetImage(image);
        }
        if (folderFile is not null)
        {
            var files = new System.Collections.Specialized.StringCollection { folderFile };
            data.SetFileDropList(files);
        }
        else if (!string.IsNullOrEmpty(clip.FilesJson))
        {
            var paths = JsonSerializer.Deserialize<string[]>(clip.FilesJson);
            if (clip.Kind == ClipKind.Files && paths is not { Length: > 0 })
                throw new FileNotFoundException("No se encontró la lista de archivos originales.");
            if (paths is { Length: > 0 })
            {
                if (paths.Any(path => !File.Exists(path) && !Directory.Exists(path)))
                    throw new FileNotFoundException("No se encontró uno de los archivos originales. Es posible que se haya movido o eliminado.");
                var files = new System.Collections.Specialized.StringCollection();
                files.AddRange(paths);
                data.SetFileDropList(files);
            }
        }

        Clipboard.SetDataObject(data, true);
    }

    public static async Task<string?> CreateFolderPasteFileAsync(ClipboardClip clip, CancellationToken cancellationToken = default)
    {
        if (clip.Kind == ClickBoard.Core.Models.ClipKind.Files)
            return null;

        var extension = clip.Kind == ClickBoard.Core.Models.ClipKind.Image && clip.ImageBytes is { Length: > 0 }
            ? ".png"
            : clip.Kind == ClickBoard.Core.Models.ClipKind.Text && clip.TextContent is not null ? ".txt" : null;
        if (extension is null)
            return null;

        var payload = extension == ".png" ? clip.ImageBytes! : new UTF8Encoding(false).GetBytes(clip.TextContent!);
        var hash = SHA256.HashData(payload);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"ClipForge-{Convert.ToHexString(hash)}{extension}");
        if (File.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("El archivo generado no es válido.");
            await using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var existingHash = await SHA256.HashDataAsync(existing, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(hash, existingHash))
                throw new IOException("El archivo generado fue modificado; no se reutilizará.");
            return path;
        }
        using var quota = await StorageQuota.AcquireAsync(payload.LongLength, cancellationToken);
        var created = false;
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            created = true;
            await output.WriteAsync(payload, cancellationToken);
            return path;
        }
        catch
        {
            if (created && File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    private static bool IsFolderDestination(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window)) return false;
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }

    private static bool SendPasteShortcut()
    {
        var inputs = new[]
        {
            KeyboardInput(0x11, 0),
            KeyboardInput(0x56, 0),
            KeyboardInput(0x56, 0x0002),
            KeyboardInput(0x11, 0x0002)
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static bool ModifiersArePressed() =>
        (GetAsyncKeyState(0x10) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x12) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static Input KeyboardInput(ushort key, uint flags) => new()
    {
        Type = 1,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInputData
            {
                VirtualKey = key,
                Flags = flags
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        [FieldOffset(0)] public MouseInputData Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
}

public sealed class PasteBusyException() : InvalidOperationException("Ya hay un pegado en curso.");
