using System.IO;
using System.Text.Json;

namespace ClickBoard.Infrastructure.Windows;

public static class OwnedFileStore
{
    public static bool DeleteCopies(string? filesJson)
    {
        if (string.IsNullOrEmpty(filesJson)) return true;
        string[] paths;
        try { paths = JsonSerializer.Deserialize<string[]>(filesJson) ?? []; }
        catch (JsonException) { return false; }

        var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "files"));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var itemDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var captureDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var success = true;
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var parts = Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out _) || parts[1].Length != 4 || !parts[1].All(char.IsAsciiDigit))
                {
                    success = false;
                    continue;
                }
                var captureDirectory = Path.Combine(root, parts[0]);
                var itemDirectory = Path.Combine(captureDirectory, parts[1]);
                var marker = Path.Combine(captureDirectory, ".clipforge-owned");
                if (IsLink(root) || IsLink(captureDirectory) || IsLink(itemDirectory) || IsLink(fullPath) || IsLink(marker) ||
                    !File.Exists(marker) || new FileInfo(marker).Length > 128 || File.ReadAllText(marker) != "ClipForge snapshot v1:" + parts[0])
                {
                    success = false;
                    continue;
                }
                if (File.Exists(fullPath)) File.Delete(fullPath);
                itemDirectories.Add(itemDirectory);
                captureDirectories.Add(captureDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                success = false;
            }
        }
        foreach (var directory in itemDirectories)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false); }
            catch (IOException) { success = false; }
            catch (UnauthorizedAccessException) { success = false; }
        }
        foreach (var directory in captureDirectories)
        {
            try
            {
                if (!Directory.Exists(directory)) continue;
                var marker = Path.Combine(directory, ".clipforge-owned");
                if (IsLink(directory) || IsLink(marker) || Directory.EnumerateFileSystemEntries(directory).Any(entry => !string.Equals(entry, marker, StringComparison.OrdinalIgnoreCase)))
                {
                    success = false;
                    continue;
                }
                File.Delete(marker);
                Directory.Delete(directory, recursive: false);
            }
            catch (IOException) { success = false; }
            catch (UnauthorizedAccessException) { success = false; }
        }
        return success;
    }

    private static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
