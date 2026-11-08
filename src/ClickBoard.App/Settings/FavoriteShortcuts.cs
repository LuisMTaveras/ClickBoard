using System.IO;
using System.Text.Json;

namespace ClickBoard.App.Settings;

public static class FavoriteShortcuts
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickBoard",
        "favorite-shortcuts.json");

    public static Dictionary<long, string> Load()
    {
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > 32768)
            throw new InvalidDataException("Demasiados atajos.");
        var values = JsonSerializer.Deserialize<Dictionary<long, string>>(File.ReadAllText(FilePath)) ?? [];
        if (values.Count > 100 || values.Any(pair => pair.Key <= 0 || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 100))
            throw new InvalidDataException("Atajos inválidos.");
        return values;
    }

    public static void Save(Dictionary<long, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(values));
            File.Move(temporary, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
