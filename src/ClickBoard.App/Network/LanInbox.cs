using System.IO;
using System.Text.Json;

namespace ClickBoard.App.Network;

internal static class LanInbox
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickBoard",
        "network",
        "inbox");

    public static void Store(LanReceipt receipt)
    {
        Directory.CreateDirectory(Root);
        var name = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        var temporary = name + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
            File.Move(temporary, name + ".json", false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static IReadOnlyList<LanReceipt> Load()
    {
        if (!Directory.Exists(Root))
            return [];

        var result = new List<LanReceipt>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(1000))
        {
            try
            {
                if (new FileInfo(path).Length > 32768)
                    continue;
                var receipt = JsonSerializer.Deserialize<LanReceipt>(File.ReadAllText(path));
                if (receipt is not null && !string.IsNullOrEmpty(receipt.FileName) && IsReceivedPath(receipt.SavedPath))
                    result.Add(receipt);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
            }
        }
        return result.OrderByDescending(receipt => receipt.ReceivedAt).ToArray();
    }

    public static bool IsReceivedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "Received") + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
