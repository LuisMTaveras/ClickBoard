using System.IO;
using System.Text.Json;

namespace ClickBoard.App.Settings;

public sealed record ProjectRule(string Project, string Process, string TitleContains);
public sealed record ProjectDetectionSettings(bool Enabled, List<ProjectRule> Rules);
public static class ProjectDetection
{
    private static readonly string PathName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "project-detection.json");
    public static ProjectDetectionSettings Current { get; private set; } = new(false, []);
    public static void Load()
    {
        if (!File.Exists(PathName)) return;
        if (new FileInfo(PathName).Length > 1_048_576) throw new InvalidDataException("Configuración de proyectos demasiado grande.");
        var settings = JsonSerializer.Deserialize<ProjectDetectionSettings>(File.ReadAllText(PathName)) ?? new(false, []);
        Validate(settings); Current = settings;
    }
    private static void Validate(ProjectDetectionSettings settings)
    {
        if (settings.Rules is null || settings.Rules.Count > 200 || settings.Rules.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Project) || rule.Project.Length > 80 || string.IsNullOrWhiteSpace(rule.Process) || rule.Process.Length > 100 || string.IsNullOrWhiteSpace(rule.TitleContains) || rule.TitleContains.Length > 200)) throw new InvalidDataException("Reglas de proyecto inválidas.");
    }
    public static void Save(ProjectDetectionSettings settings)
    {
        Validate(settings); Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings)); File.Move(temporary, PathName, true); Current = settings; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string[] Match(string process, string title) => Current.Rules.Where(rule => string.Equals(rule.Process.Trim().RemovesExe(), process, StringComparison.OrdinalIgnoreCase) && title.Contains(rule.TitleContains, StringComparison.OrdinalIgnoreCase)).Select(rule => rule.Project).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string RemovesExe(this string process) => process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process[..^4] : process;
}
