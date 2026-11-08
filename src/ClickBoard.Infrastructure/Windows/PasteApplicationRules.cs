using System.IO;
using System.Text.Json;

namespace ClickBoard.Infrastructure.Windows;

public static class PasteApplicationRules
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickBoard",
        "plain-paste-apps.json");
    private static readonly object SyncLock = new();
    private static HashSet<string>? _processes;

    public static string[] GetProcesses()
    {
        lock (SyncLock)
        {
            if (_processes is null)
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 32768)
                    throw new InvalidDataException("Reglas demasiado grandes.");
                var loaded = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(FilePath)) ?? []
                    : [];
                _processes = new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            return _processes.ToArray();
        }
    }

    public static void SetProcesses(IEnumerable<string> processes)
    {
        var values = processes
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Select(value => value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length > 100 || values.Any(value => value.Length > 100 || value.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new ArgumentException("Nombres de proceso inválidos.");

        lock (SyncLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(values));
                File.Move(temporary, FilePath, true);
                _processes = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    public static bool UsePlainText(IntPtr target)
    {
        var processName = ForegroundWindowTracker.GetProcessName(target);
        return processName is not null && GetProcesses().Contains(processName, StringComparer.OrdinalIgnoreCase);
    }
}
