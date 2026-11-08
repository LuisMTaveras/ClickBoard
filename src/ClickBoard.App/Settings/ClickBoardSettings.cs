using System.IO;
using System.Text.Json;

namespace ClickBoard.App.Settings;

public enum QuickPasteAction
{
    PreserveFormatting,
    PlainText
}

public sealed record ClickBoardSettings
{
    public string Theme { get; init; } = "Dark";
    public string QuickPickerHotkey { get; init; } = "Ctrl+Shift+Space";
    public string DockedPanelHotkey { get; init; } = "Ctrl+Alt+Space";
    public QuickPasteAction QuickEnterAction { get; init; } = QuickPasteAction.PreserveFormatting;
    public bool EnableAlternatePasteAction { get; init; } = true;
    public bool EnableFavoriteQuickAction { get; init; } = true;
    public int MaxFileSizeMb { get; init; } = 50;
    public int MaxStorageMb { get; init; } = 2048;
    public bool CapturePaused { get; init; }
    public string ExcludedApplications { get; init; } = string.Empty;
    public bool CloseQuickOnFocusLoss { get; init; }
    public int TrashRetentionDays { get; init; } = 0;
}

public static class ClickBoardSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickBoard",
        "settings.json");

    public static ClickBoardSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new ClickBoardSettings();

            return JsonSerializer.Deserialize<ClickBoardSettings>(File.ReadAllText(SettingsPath))
                ?? new ClickBoardSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClickBoardSettings();
        }
    }

    public static void Save(ClickBoardSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
