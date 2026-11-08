using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using ClickBoard.Core.Models;

namespace ClickBoard.App;

public partial class MainWindow
{
    private async void ExportBatch_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClipList.Items.OfType<ClipboardClip>().Where(clip => ClipList.SelectedItems.Contains(clip)).ToArray();
        if (selected.Length == 0) { _viewModel.StatusMessage = "Selecciona clips para exportar."; return; }
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Exportar selección a una carpeta" };
        if (dialog.ShowDialog(this) != true) return;
        var exported = 0;
        try
        {
            foreach (var selectedClip in selected)
            {
                var clip = await _viewModel.LoadClipAsync(selectedClip.Id) ?? throw new IOException("Clip no disponible.");
                if (clip.Kind == ClipKind.Files)
                {
                    var paths = JsonSerializer.Deserialize<string[]>(clip.FilesJson ?? "[]") ?? [];
                    if (paths.Length == 0) throw new IOException("No hay archivos.");
                    foreach (var path in paths)
                    {
                        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                        await ExportStreamAsync(input, dialog.FolderName, Path.GetFileName(path)); exported++;
                    }
                }
                else
                {
                    var payload = clip.Kind == ClipKind.Image ? clip.ImageBytes : clip.TextContent is { } text ? new UTF8Encoding(false).GetBytes(text) : null;
                    if (payload is null) throw new IOException("Contenido no disponible.");
                    var title = new string((clip.Title ?? $"ClipForge-{clip.Id}").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(80).ToArray()).Trim().TrimEnd('.');
                    if (title.Length == 0) title = $"ClipForge-{clip.Id}";
                    // Prefix protects reserved Windows device names in custom titles.
                    title = "Clip-" + title + (clip.Kind == ClipKind.Image ? ".png" : ".txt");
                    await using var input = new MemoryStream(payload);
                    await ExportStreamAsync(input, dialog.FolderName, title); exported++;
                }
            }
            _viewModel.StatusMessage = $"{exported} archivos exportados sin sobrescribir existentes.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or Microsoft.Data.Sqlite.SqliteException)
        { _viewModel.StatusMessage = $"Exportación interrumpida: {exported} archivos completados. Los archivos ya exportados se conservan."; }
    }

    private static async Task ExportStreamAsync(Stream input, string folder, string name)
    {
        var temporary = Path.Combine(folder, $".clipforge-{Guid.NewGuid():N}.partial");
        var created = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            { created = true; await input.CopyToAsync(output); }
            for (var suffix = 0; suffix < 10000; suffix++)
            {
                var destination = Path.Combine(folder, suffix == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}");
                try { File.Move(temporary, destination, false); return; }
                catch (IOException) when (File.Exists(destination) || Directory.Exists(destination)) { }
            }
            throw new IOException("Demasiados nombres repetidos en destino.");
        }
        finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
    }
}
