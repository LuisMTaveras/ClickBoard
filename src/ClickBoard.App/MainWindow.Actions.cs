using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClickBoard.Infrastructure.Windows;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace ClickBoard.App;

public partial class MainWindow
{
    private void OpenPasteRules_Click(object sender, RoutedEventArgs e)
    {
        string[] processes;
        try { processes = PasteApplicationRules.GetProcesses(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { _viewModel.StatusMessage = "No se pudieron leer las reglas de pegado. El archivo se conserva."; return; }
        var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 650 };
        panel.Children.Add(new TextBlock { Text = "Reglas por aplicación", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "Pegar como texto plano en estos procesos (separados por coma). Por ejemplo: WindowsTerminal, powershell, cmd. El resto conserva el formato del clip. Las imágenes y archivos conservan su tipo.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        var box = new TextBox { Style = (Style)FindResource("SearchBox"), Text = string.Join(", ",processes), MaxLength = 10000 }; panel.Children.Add(box);
        var status = new TextBlock { Text = "Los cambios se guardan automáticamente tras una pausa al escribir.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) }; panel.Children.Add(status);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        void Commit()
        {
            timer.Stop();
            try { PasteApplicationRules.SetProcesses(box.Text.Split([',',';','\n'],StringSplitOptions.RemoveEmptyEntries)); status.Text = "Reglas aplicadas."; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { status.Text = "No se pudo guardar. Introduce nombres de proceso válidos; se mantienen las reglas anteriores."; }
        }
        timer.Tick += (_, _) => Commit(); box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); }; box.LostKeyboardFocus += (_, _) => Commit();
        var back = new Button { Content = "Volver", Style = (Style)FindResource("SecondaryButton") }; back.Click += (_, _) => { Commit(); ClosePage(); }; panel.Children.Add(back);
        panel.Unloaded += (_, _) => { if(timer.IsEnabled) Commit(); timer.Stop(); };
        ShowPage(panel);
    }
    private void OpenActions_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 650 };
        panel.Children.Add(new TextBlock { Text = "Acciones", FontSize = 22, Margin = new Thickness(0,0,0,12) });
        var query = new TextBox { Style = (Style)FindResource("SearchBox"), ToolTip = "Buscar una acción" }; panel.Children.Add(query);
        var rows = new StackPanel { Margin = new Thickness(0,12,0,0) }; panel.Children.Add(rows);
        var actions = new (string Title, Action Run)[]
        {
            ("Exportar selección", () => { ClosePage(); ExportBatch_Click(this,new()); }),
            ("Enviar selección a un dispositivo", () => { ClosePage(); SendBatch_Click(this,new()); }),
            ("Mover selección a un proyecto", () => { ClosePage(); OpenProjects_Click(this,new()); }),
            ("Comparar textos seleccionados", () => { ClosePage(); CompareSelected_Click(this,new()); }),
            ("Preparar cola de pegado", () => { ClosePage(); PrepareQueue_Click(this,new()); }),
            ("Recetas de transformación", () => OpenRecipes_Click(this,new())),
            ("Papelera", () => OpenTrash_Click(this,new())),
            ("Reglas de pegado", () => OpenPasteRules_Click(this,new())),
            ("Atajos de favoritos", () => OpenFavoriteShortcuts_Click(this,new())),
            ("Filtros avanzados", () => AdvancedFilters_Click(this,new())),
            ("Volver al historial", ClosePage)
        };
        void Render()
        {
            rows.Children.Clear();
            foreach(var action in actions.Where(action => action.Title.Contains(query.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)))
            { var button = new Button { Content = action.Title, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0,0,0,6) }; button.Click += (_, _) => action.Run(); rows.Children.Add(button); }
            if(rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No hay acciones coincidentes." });
        }
        query.TextChanged += (_, _) => Render();
        query.PreviewKeyDown += (_, key) => { if(key.Key == System.Windows.Input.Key.Enter && rows.Children.OfType<Button>().FirstOrDefault() is { } first) { key.Handled = true; first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } };
        Render(); ShowPage(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); query.Focus();
    }
}
