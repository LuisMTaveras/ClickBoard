using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClickBoard.Core.Models;
using ClickBoard.Infrastructure.Windows;

namespace ClickBoard.App;

public partial class MainWindow
{
    private System.Windows.Point _dragOrigin;
    private bool _preparingDrag;
    private void InitializeDragging()
    {
        ClipList.PreviewMouseLeftButtonDown += (_, e) => _dragOrigin = e.GetPosition(ClipList);
        ClipList.PreviewMouseMove += async (_, e) =>
        {
            if (_preparingDrag || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(ClipList);
            if (Math.Abs(position.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(ClipList,source) is not ListBoxItem) return;
            var selected = ClipList.SelectedItems.OfType<ClipboardClip>().ToArray();
            if (selected.Length == 0 || selected.Any(clip => clip.Kind == ClipKind.Text)) return;
            _preparingDrag = true;
            try
            {
                var paths = new List<string>();
                foreach (var selectedClip in selected)
                {
                    var clip = await _viewModel.LoadClipAsync(selectedClip.Id) ?? throw new IOException("Clip no disponible.");
                    if (clip.Kind == ClipKind.Files) paths.AddRange(JsonSerializer.Deserialize<string[]>(clip.FilesJson ?? "[]") ?? []);
                    else if (await WindowsPasteService.CreateFolderPasteFileAsync(clip) is { } image) paths.Add(image);
                }
                if (paths.Count == 0 || paths.Any(path => !File.Exists(path))) throw new IOException("Archivo no disponible.");
                if (Mouse.LeftButton != MouseButtonState.Pressed) return;
                var data = new System.Windows.DataObject(); var files = new System.Collections.Specialized.StringCollection(); files.AddRange(paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()); data.SetFileDropList(files);
                System.Windows.DragDrop.DoDragDrop(ClipList,data,System.Windows.DragDropEffects.Copy);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or Microsoft.Data.Sqlite.SqliteException or System.Runtime.InteropServices.COMException)
            { _viewModel.StatusMessage = "No se pudo preparar el arrastre. Comprueba los archivos seleccionados."; }
            finally { _preparingDrag = false; }
        };
    }
}
