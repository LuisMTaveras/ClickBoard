using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Text;
using System.Text.Json;
using ClickBoard.Core.Models;
using ClickBoard.App.ViewModels;

namespace ClickBoard.App;

public partial class MainWindow : Window
{
    private readonly ClipboardViewModel _viewModel;
    private bool _allowClose;

    public event EventHandler? QuickPickerRequested;
    public event EventHandler? DockedPanelRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? NetworkRequested;
    public event EventHandler? ClipTitleChanged;
    public event EventHandler? ClipDeleted;
    internal int TrashRetentionDays { get; set; }
    internal Func<int, string?>? SaveTrashRetention { get; set; }
    public event Action<IReadOnlyList<ClipboardClip>>? QueueRequested;
    private void PrepareQueue_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClipList.Items.OfType<ClipboardClip>().Where(clip => ClipList.SelectedItems.Contains(clip)).ToArray();
        if (selected.Length == 0) { _viewModel.StatusMessage = "Selecciona clips para preparar la cola."; return; }
        QueueRequested?.Invoke(selected);
    }
    private void ClearQueue_Click(object sender, RoutedEventArgs e) => QueueRequested?.Invoke(Array.Empty<ClipboardClip>());

    public MainWindow(ClipboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateNavigationSelection();
        Activated += (_, _) => _viewModel.RefreshDestination();
        InitializeDragging();
    }

    public void ShowFromTray()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    public void SetCapturePaused(bool paused) => CaptureStateText.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    public object? CurrentPage => SettingsContent.Visibility == Visibility.Visible ? SettingsContent.Content : null;

    public void ShowPage(UIElement page)
    {
        SettingsContent.Content = page;
        Workspace.Visibility = Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Visible;
    }

    public void ClosePage()
    {
        SettingsContent.Visibility = Visibility.Collapsed;
        SettingsContent.Content = null;
        Workspace.Visibility = Visibility.Visible;
    }

    internal System.Windows.Controls.ListBox CreateThemedListBox(Thickness? margin = null, double? height = null, System.Windows.Controls.SelectionMode selectionMode = System.Windows.Controls.SelectionMode.Single)
    {
        var list = new System.Windows.Controls.ListBox
        {
            SelectionMode = selectionMode,
            Margin = margin ?? new Thickness(0),
            BorderThickness = new Thickness(1)
        };
        if (height.HasValue)
            list.Height = height.Value;
        list.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        list.SetResourceReference(ForegroundProperty, "TextBrush");
        list.SetResourceReference(BorderBrushProperty, "LineBrush");
        return list;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string section })
        {
            await _viewModel.SetSectionAsync(section);
            UpdateNavigationSelection();
        }
    }

    private void UpdateNavigationSelection()
    {
        var buttons = new[] { ClipboardNavButton, FavoritesNavButton, DeveloperNavButton, ImagesNavButton };
        foreach (var button in buttons)
        {
            var isSelected = Equals(button.Tag, _viewModel.CurrentSection);
            if (isSelected)
            {
                button.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                button.SetResourceReference(ForegroundProperty, "AccentBrush");
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.SetResourceReference(ForegroundProperty, "TextBrush");
            }
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void OpenQuickPicker_Click(object sender, RoutedEventArgs e) => QuickPickerRequested?.Invoke(this, EventArgs.Empty);
    private void OpenDockedPanel_Click(object sender, RoutedEventArgs e) => DockedPanelRequested?.Invoke(this, EventArgs.Empty);
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void OpenNetwork_Click(object sender, RoutedEventArgs e) => NetworkRequested?.Invoke(this, EventArgs.Empty);

    private async void PasteSelected_Click(object sender, RoutedEventArgs e) => await _viewModel.PasteSelectedAsync();
    private async void PasteSelectedPlainText_Click(object sender, RoutedEventArgs e) => await _viewModel.PasteSelectedPlainTextAsync();
    private void ToggleJsonFormatting_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.ToggleJsonFormatting(); }
        catch (JsonException) { _viewModel.StatusMessage = "El contenido no se pudo interpretar como JSON."; }
    }
    private async void ClipList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(ClipList, source) is not ListBoxItem item)
            return;
        ClipList.SelectedItem = item.DataContext;
        await _viewModel.PasteSelectedAsync();
    }

    private async void ClipList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await DeleteSelectedAsync();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _viewModel.PasteSelectedAsync();
        }
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e) => await _viewModel.ToggleFavoriteAsync(_viewModel.SelectedClip);

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        var selected = ClipList.SelectedItems.OfType<ClipboardClip>().ToArray();
        if (selected.Length == 0 && _viewModel.SelectedClip is { } current) selected = [current];
        if (selected.Length == 0) return;
        var description = selected.Length == 1 ? $"«{selected[0].ShortPreview}»" : $"{selected.Length} clips";
        var result = System.Windows.MessageBox.Show(this,
            $"¿Enviar {description} a la papelera? Podrás restaurarlos después.",
            "Enviar a la papelera", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var deleted = 0;
            foreach (var clip in selected) if (await _viewModel.DeleteClipAsync(clip)) deleted++;
            if (deleted > 0) ClipDeleted?.Invoke(this, EventArgs.Empty);
            _viewModel.StatusMessage = $"{deleted} clips enviados a la papelera.";
        }
        catch (Exception) { _viewModel.StatusMessage = "No se pudo eliminar el clip. Inténtalo de nuevo."; }
    }

    private async void OpenTrash_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clips = await _viewModel.GetTrashAsync();
            var list = CreateThemedListBox(new Thickness(12), selectionMode: System.Windows.Controls.SelectionMode.Extended);
            list.DisplayMemberPath = "ShortPreview";
            list.ItemsSource = clips;
            var restore = new System.Windows.Controls.Button { Content = "Restaurar selección", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(6) };
            var delete = new System.Windows.Controls.Button { Content = "Eliminar definitivamente…", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(6) };
            var back = new System.Windows.Controls.Button { Content = "Volver", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(6) };
            var status = new TextBlock { Text = "Papelera · Ctrl o Mayús para seleccionar varios. Se muestran hasta 1000 clips.", Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
            var buttons = new WrapPanel(); buttons.Children.Add(restore); buttons.Children.Add(delete); buttons.Children.Add(back);
            var panel = new DockPanel { Margin = new Thickness(20) }; DockPanel.SetDock(status, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(status); panel.Children.Add(buttons); panel.Children.Add(list);
            var retentionPanel = new StackPanel { Margin = new Thickness(12) };
            retentionPanel.Children.Add(new TextBlock { Text = "Conservación de la papelera", FontWeight = FontWeights.SemiBold });
            retentionPanel.Children.Add(new TextBlock { Text = "Al elegir un plazo se borrarán automáticamente los clips que lo superen, incluidos los que ya están en la papelera. El plazo se cuenta desde su eliminación. Nunca es la opción inicial.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,6) });
            var retention = new System.Windows.Controls.ComboBox { ItemsSource = new[] { "Nunca borrar automáticamente", "7 días", "30 días", "90 días" }, SelectedIndex = TrashRetentionDays switch { 7 => 1, 30 => 2, 90 => 3, _ => 0 }, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, MinWidth = 240 };
            var restoringRetention = false;
            retention.SelectionChanged += (_, _) =>
            {
                if (restoringRetention) return;
                var days = retention.SelectedIndex switch { 1 => 7, 2 => 30, 3 => 90, _ => 0 };
                var error = SaveTrashRetention?.Invoke(days) ?? (SaveTrashRetention is null ? "No se pudo guardar la preferencia." : null);
                if (error is not null) { status.Text = error; restoringRetention = true; retention.SelectedIndex = TrashRetentionDays switch { 7 => 1, 30 => 2, 90 => 3, _ => 0 }; restoringRetention = false; }
                else status.Text = days == 0 ? "Borrado automático desactivado." : $"Plazo aplicado: {days} días. Se revisa cada cinco minutos mientras la aplicación está abierta.";
            };
            retentionPanel.Children.Add(retention); DockPanel.SetDock(retentionPanel, Dock.Top); panel.Children.Insert(1, retentionPanel);
            async Task Run(bool permanent)
            {
                var selected = list.SelectedItems.OfType<ClipboardClip>().ToArray();
                if (selected.Length == 0) { status.Text = "Selecciona uno o más clips."; return; }
                if (permanent && System.Windows.MessageBox.Show(this, $"¿Eliminar definitivamente {selected.Length} clips y sus copias locales identificadas como propias? Esta acción no se puede deshacer.", "Borrado definitivo", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                restore.IsEnabled = delete.IsEnabled = false;
                var completed = 0;
                try
                {
                    foreach (var clip in selected)
                    {
                        if (permanent) { if (await _viewModel.PermanentlyDeleteAsync(clip)) completed++; }
                        else { await _viewModel.RestoreAsync(clip.Id); completed++; }
                    }
                    status.Text = $"{completed} clips procesados. Las copias que no se pudieron eliminar se conservan.";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException or Microsoft.Data.Sqlite.SqliteException)
                { status.Text = $"Operación interrumpida tras {completed} clips. Comprueba si el portapapeles usa alguno de los archivos."; }
                finally
                {
                    restore.IsEnabled = delete.IsEnabled = true;
                    try { list.ItemsSource = await _viewModel.GetTrashAsync(); ClipDeleted?.Invoke(this, EventArgs.Empty); }
                    catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException) { status.Text = "No se pudo actualizar la papelera. Vuelve a abrirla."; }
                }
            }
            restore.Click += async (_, _) => await Run(false); delete.Click += async (_, _) => await Run(true);
            back.Click += (_, _) => ClosePage(); ShowPage(panel);
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException)
        { _viewModel.StatusMessage = "No se pudo abrir la papelera."; }
    }

    private async void SaveTitle_Click(object sender, RoutedEventArgs e) => await SaveTitleAsync();
    private async void LoadMore_Click(object sender, RoutedEventArgs e) => await _viewModel.LoadMoreAsync();
    private void AdvancedFilters_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 600, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "Filtros avanzados", FontSize = 22, Margin = new Thickness(0,0,0,16) });
        var fields = new List<(string Key, System.Windows.Controls.TextBox Box)>();
        foreach (var (key, label) in new[] { ("", "Contenido o título"), ("tipo", "Tipo: texto, imagen, archivo o extensión (pdf, zip…)"), ("etiqueta", "Etiqueta"), ("app", "Aplicación de origen"), ("proyecto", "Proyecto / colección"), ("antes", "Antes de: AAAA-MM-DD (UTC)") })
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,8,0,5) });
            var box = new System.Windows.Controls.TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 200 };
            panel.Children.Add(box); fields.Add((key, box));
        }
        var apply = new System.Windows.Controls.Button { Content = "Aplicar filtros", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0,18,0,8) };
        apply.Click += (_, _) =>
        {
            var date = fields.Last().Box.Text.Trim();
            if (date.Length > 0 && !DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            { fields.Last().Box.Focus(); fields.Last().Box.ToolTip = "Usa una fecha válida AAAA-MM-DD."; return; }
            _viewModel.SearchText = string.Join(" ", fields.Where(field => !string.IsNullOrWhiteSpace(field.Box.Text)).Select(field => field.Key.Length == 0 ? field.Box.Text.Trim() : field.Key + ":\"" + field.Box.Text.Trim().Replace("\"", "") + "\""));
            ClosePage();
        };
        var cancel = new System.Windows.Controls.Button { Content = "Volver", Style = (Style)FindResource("SecondaryButton") };
        cancel.Click += (_, _) => ClosePage();
        panel.Children.Add(apply); panel.Children.Add(cancel);
        ShowPage(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    private async void CopySelected_Click(object sender, RoutedEventArgs e) => await _viewModel.CopySelectedAsync();
    private async void CombineSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClipList.Items.OfType<ClipboardClip>().Where(clip => ClipList.SelectedItems.Contains(clip)).ToArray();
        var separator = CombineSeparator.SelectedIndex switch { 1 => Environment.NewLine, 2 => ", ", 3 => "\t", 4 => "", _ => Environment.NewLine + Environment.NewLine };
        await _viewModel.CombineTextClipsAsync(selected, separator);
    }
    private async void CompareSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = ClipList.Items.OfType<ClipboardClip>().Where(clip => ClipList.SelectedItems.Contains(clip)).ToArray();
        var comparison = await _viewModel.GetComparisonAsync(selected);
        if (comparison is not { } pair) return;
        if (pair.Left.Length + (long)pair.Right.Length > 2_000_000)
        { _viewModel.StatusMessage = "La comparación admite hasta dos millones de caracteres en total. Exporta los clips para comparar archivos mayores."; return; }
        if (pair.Left.Count(c => c is '\r' or '\n') + (long)pair.Right.Count(c => c is '\r' or '\n') > 20_000)
        { _viewModel.StatusMessage = "Los clips tienen demasiadas líneas para esta vista. Exporta los archivos para compararlos."; return; }
        _viewModel.StatusMessage = "Calculando diferencias…";
        var result = await Task.Run(() => LineComparison.Create(pair.Left, pair.Right));
        var view = new ComparisonView(result, selected[0].DisplayTitle, selected[1].DisplayTitle);
        view.CloseRequested += (_, _) => ClosePage();
        ShowPage(view);
        _viewModel.StatusMessage = "Comparación preparada. El contenido original no se modifica.";
    }
    private async void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clip = await _viewModel.GetSelectedContentAsync();
            if (clip is null) return;
            if (clip.Kind == ClipKind.Files)
            {
                var paths = JsonSerializer.Deserialize<string[]>(clip.FilesJson ?? "[]") ?? [];
                if (paths.Length == 0 || paths.Any(path => !File.Exists(path)))
                { _viewModel.StatusMessage = "La exportación requiere archivos disponibles; no admite carpetas en esta versión."; return; }
                var folder = new Microsoft.Win32.OpenFolderDialog { Title = "Elegir carpeta para exportar" };
                if (folder.ShowDialog(this) != true) return;
                var exported = 0;
                try
                {
                    foreach (var source in paths)
                    {
                        var name = Path.GetFileName(source);
                        var temp = Path.Combine(folder.FolderName, $".clipforge-{Guid.NewGuid():N}.partial");
                        var created = false;
                        try
                        {
                            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            { created = true; await input.CopyToAsync(output); }
                            for (var index = 0; ; index++)
                            {
                                var candidate = Path.Combine(folder.FolderName, index == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({index}){Path.GetExtension(name)}");
                                try { File.Move(temp, candidate, overwrite: false); break; }
                                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
                            }
                            exported++;
                        }
                        finally { if (created && File.Exists(temp)) File.Delete(temp); }
                    }
                    _viewModel.StatusMessage = $"{exported} archivo(s) exportado(s).";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { _viewModel.StatusMessage = $"Se exportaron {exported} de {paths.Length} archivos. No se pudo completar la operación."; }
                return;
            }
            var image = clip.Kind == ClipKind.Image;
            var extension = image ? ".png" : ".txt";
            var safeTitle = new string((clip.Title ?? "ClipForge").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(80).ToArray()).Trim();
            if (safeTitle.Length == 0) safeTitle = "ClipForge";
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Exportar clip", FileName = safeTitle + extension, DefaultExt = extension, AddExtension = true, OverwritePrompt = true, Filter = image ? "Imagen PNG|*.png" : "Texto UTF-8|*.txt" };
            if (dialog.ShowDialog(this) != true) return;
            if (image && !Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
            { _viewModel.StatusMessage = "La imagen debe exportarse con extensión .png."; return; }
            var payload = image ? clip.ImageBytes : clip.TextContent is { } text ? new UTF8Encoding(false).GetBytes(text) : null;
            if (payload is null) { _viewModel.StatusMessage = "No hay contenido exportable."; return; }
            var temporary = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, $".clipforge-{Guid.NewGuid():N}.partial");
            var temporaryCreated = false;
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                { temporaryCreated = true; await output.WriteAsync(payload); }
                File.Move(temporary, dialog.FileName, overwrite: true);
            }
            finally { if (temporaryCreated && File.Exists(temporary)) File.Delete(temporary); }
            _viewModel.StatusMessage = "Clip exportado.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { _viewModel.StatusMessage = "No se pudo exportar el clip. Comprueba la carpeta de destino."; }
    }
    private void TransformText_Click(object sender, RoutedEventArgs e)
    {
        try { if (sender is System.Windows.Controls.Button { Tag: string action }) _viewModel.TransformText(action); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { _viewModel.StatusMessage = "La plantilla requiere demasiado tiempo para procesarse."; }
    }
    private void UndoTransform_Click(object sender, RoutedEventArgs e) => _viewModel.UndoTransform();

    private async void TitleBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SaveTitleAsync();
    }

    private async Task SaveTitleAsync()
    {
        try
        {
            await _viewModel.SaveTitleAsync();
            ClipTitleChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            _viewModel.StatusMessage = "No se pudo guardar el título. Inténtalo de nuevo.";
        }
    }

    private async void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        { e.Handled = true; if (!_batchSending) OpenActions_Click(this, new RoutedEventArgs()); return; }
        if (SettingsContent.Visibility == Visibility.Visible) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && ReferenceEquals(e.OriginalSource, MainSearchBox))
        {
            e.Handled = true;
            await _viewModel.PasteSelectedAsync();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            e.Handled = true;
            MainSearchBox.Focus();
            MainSearchBox.SelectAll();
        }
    }
}
