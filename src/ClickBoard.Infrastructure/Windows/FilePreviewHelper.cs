using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ClickBoard.Infrastructure.Windows;

public static class FilePreviewHelper
{
    private static readonly Regex PdfPageTypePattern = new(
        @"/Type\s*/Page\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex PdfRootCountPattern = new(
        @"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(250));

    public static string BuildFilesSummary(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return "Archivos no disponibles";

        if (paths.Count == 1)
            return DescribeSingleFile(paths[0]);

        long totalBytes = 0;
        var lines = new List<string>(paths.Count + 2);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name))
                name = path;

            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    totalBytes += info.Length;
                    var details = GetFileDetailsLabel(path, info.Length);
                    lines.Add($"• {name}  ({details})");
                }
                else
                {
                    lines.Add($"• {name}  (no disponible)");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                lines.Add($"• {name}");
            }
        }

        var header = totalBytes > 0
            ? $"{paths.Count} archivos · Tamaño total: {FormatSize(totalBytes)}"
            : $"{paths.Count} archivos";
        lines.Insert(0, header);
        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildShortPreview(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return "Archivos copiados";

        if (paths.Count == 1)
        {
            var path = paths[0];
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    var details = GetFileDetailsLabel(path, info.Length);
                    return $"{name} · {details}";
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
            return string.IsNullOrEmpty(name) ? path : name;
        }

        var firstFileName = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        long totalBytes = 0;
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                    totalBytes += info.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        return totalBytes > 0
            ? $"{paths.Count} archivos ({firstFileName}…) · {FormatSize(totalBytes)}"
            : $"{paths.Count} archivos ({firstFileName}…)";
    }

    private static string DescribeSingleFile(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            name = path;

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return $"Nombre original: {name}{Environment.NewLine}Estado: Archivo no disponible";

            var ext = info.Extension.ToLowerInvariant();
            var builder = new StringBuilder();
            builder.AppendLine($"Nombre original: {name}");
            builder.AppendLine($"Tamaño: {FormatSize(info.Length)} ({info.Length:N0} bytes)");
            if (ext == ".pdf")
            {
                var pages = TryCountPdfPages(path);
                builder.AppendLine(pages > 0
                    ? $"Tipo: Documento PDF · {pages} {(pages == 1 ? "página" : "páginas")}"
                    : "Tipo: Documento PDF");
            }
            else if (!string.IsNullOrEmpty(ext))
            {
                builder.AppendLine($"Tipo: Archivo {ext.TrimStart('.').ToUpperInvariant()}");
            }
            return builder.ToString().TrimEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"Nombre original: {name}";
        }
    }

    private static string GetFileDetailsLabel(string path, long length)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pdf")
        {
            var pages = TryCountPdfPages(path);
            if (pages > 0)
                return $"PDF · {pages} {(pages == 1 ? "pág." : "págs.")} · {FormatSize(length)}";
            return $"PDF · {FormatSize(length)}";
        }
        return FormatSize(length);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0.#} KB", bytes / 1024.0),
        < 1024L * 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0.##} MB", bytes / (1024.0 * 1024.0)),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:0.##} GB", bytes / (1024.0 * 1024.0 * 1024.0))
    };

    public static int TryCountPdfPages(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 64L * 1024 * 1024)
                return 0;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
            var header = new byte[5];
            if (stream.Read(header, 0, 5) < 5 || Encoding.ASCII.GetString(header) != "%PDF-")
                return 0;

            stream.Position = 0;
            var buffer = new byte[Math.Min(info.Length, 8L * 1024 * 1024)];
            var read = stream.Read(buffer, 0, buffer.Length);
            var content = Encoding.Latin1.GetString(buffer, 0, read);
            var count = PdfPageTypePattern.Count(content);
            if (count > 0)
                return count;

            var match = PdfRootCountPattern.Match(content);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rootCount) && rootCount > 0)
                return rootCount;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or RegexMatchTimeoutException or ArgumentException)
        {
        }
        return 0;
    }

    public static byte[]? TryExtractPreviewImage(string path, int maxDimension = 640)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var fullPath = Path.GetFullPath(path);
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            var hr = SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory is null)
                return null;

            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                hr = factory.GetImage(new SIZE(maxDimension, maxDimension), SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return null;

                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                using var output = new MemoryStream();
                encoder.Save(output);
                return output.ToArray();
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SIZE(int cx, int cy)
    {
        public readonly int cx = cx;
        public readonly int cy = cy;
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }
}

