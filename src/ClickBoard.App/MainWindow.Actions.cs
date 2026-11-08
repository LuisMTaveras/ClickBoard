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
        try
        {
            processes = PasteApplicationRules.GetProcesses();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _viewModel.StatusMessage = "No se pudieron leer las reglas de pegado. El archivo se conserva.";
            return;
        }

        var (rootLayout, _, cardBody) = CreateSubpageLayout(
            " Herramientas",
            "Reglas de pegado por aplicación",
            "Pega automáticamente como texto plano en estos procesos (separados por coma), por ejemplo: WindowsTerminal, powershell, cmd.");

        cardBody.Children.Add(new TextBlock { Text = "Procesos con texto plano predeterminado", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox
        {
            Style = (Style)FindResource("SearchBox"),
            Text = string.Join(", ", processes),
            MaxLength = 10000
        };
        cardBody.Children.Add(box);
        var status = new TextBlock
        {
            Text = "Los cambios se guardan automáticamente tras una pausa al escribir.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 14)
        };
        status.SetResourceReference(ForegroundProperty, "MutedBrush");
        cardBody.Children.Add(status);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        void Commit()
        {
            timer.Stop();
            try
            {
                PasteApplicationRules.SetProcesses(box.Text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries));
                status.Text = "Reglas aplicadas.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                status.Text = "No se pudo guardar. Introduce nombres de proceso válidos; se mantienen las reglas anteriores.";
            }
        }

        timer.Tick += (_, _) => Commit();
        box.TextChanged += (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        box.LostKeyboardFocus += (_, _) => Commit();

        var back = new Button { Content = "←  Volver al historial", Style = (Style)FindResource("SecondaryButton"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        back.Click += (_, _) =>
        {
            Commit();
            ClosePage();
        };
        cardBody.Children.Add(back);
        cardBody.Unloaded += (_, _) =>
        {
            if (timer.IsEnabled)
                Commit();
            timer.Stop();
        };
        ShowPage(rootLayout);
    }

    private async void OpenActions_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = ClipList.SelectedItems.OfType<ClickBoard.Core.Models.ClipboardClip>().Select(clip => clip.Id).ToArray();
        IReadOnlyList<string> projects = [];
        try
        {
            projects = await _viewModel.GetProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
        }

        var peers = TransferService?.IsRunning == true ? TransferService.GetPeers() : [];

        var (rootLayoutPalette, _, cardBodyPalette) = CreateSubpageLayout(
            "Paleta de comandos",
            "Acciones rápidas (Ctrl + K)",
            "Ejecuta comandos, envía clips a dispositivos de tu red local o cambia de espacio de proyecto con el teclado.");

        var query = new TextBox
        {
            Style = (Style)FindResource("SearchBox"),
            ToolTip = "Busca una acción, un dispositivo de red o un proyecto"
        };
        cardBodyPalette.Children.Add(query);
        var rows = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        cardBodyPalette.Children.Add(rows);

        var actions = new List<(string Title, Action Run)>
        {
            (_viewModel.IsTemporaryMode ? "Salir de modo temporal" : "Activar modo temporal", () => { ClosePage(); ToggleTemporaryMode_Click(this, new()); }),
            ("Exportar selección", () => { ClosePage(); ExportBatch_Click(this, new()); }),
            ("Enviar selección a un dispositivo", () => { ClosePage(); SendBatch_Click(this, new()); }),
            ("Mover selección a un proyecto", () => { ClosePage(); OpenProjects_Click(this, new()); }),
            ("Comparar textos seleccionados", () => { ClosePage(); CompareSelected_Click(this, new()); }),
            ("Preparar cola de pegado", () => { ClosePage(); PrepareQueue_Click(this, new()); }),
            ("Recetas de transformación", () => OpenRecipes_Click(this, new())),
            ("Papelera", () => OpenTrash_Click(this, new())),
            ("Reglas de pegado", () => OpenPasteRules_Click(this, new())),
            ("Atajos de favoritos", () => OpenFavoriteShortcuts_Click(this, new())),
            ("Filtros avanzados", () => AdvancedFilters_Click(this, new()))
        };

        foreach (var peer in peers)
        {
            var capturedPeer = peer;
            actions.Add(($"Enviar selección a {capturedPeer.Name} ({capturedPeer.Address})", () =>
            {
                ClosePage();
                OpenSendBatch(capturedPeer, autoStart: true);
            }));
        }

        if (_viewModel.ActiveProject is not null)
        {
            actions.Add(("Ver todos los proyectos", async () =>
            {
                await _viewModel.SelectProjectAsync(null);
                ProjectChanged?.Invoke(null);
                ClosePage();
            }));
        }

        foreach (var project in projects)
        {
            var capturedProject = project;
            actions.Add(($"Abrir proyecto: {capturedProject}", async () =>
            {
                await _viewModel.SelectProjectAsync(capturedProject);
                ProjectChanged?.Invoke(capturedProject);
                ClosePage();
            }));
            if (selectedIds.Length > 0)
            {
                actions.Add(($"Mover selección al proyecto: {capturedProject}", async () =>
                {
                    await _viewModel.MoveToProjectAsync(selectedIds, capturedProject);
                    ClipTitleChanged?.Invoke(this, EventArgs.Empty);
                    ClosePage();
                }));
            }
        }

        actions.Add(("Volver al historial", ClosePage));

        void Render()
        {
            rows.Children.Clear();
            var search = query.Text.Trim();
            var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var action in actions.Where(action => terms.All(term => action.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase))))
            {
                var button = new Button
                {
                    Content = action.Title,
                    Style = (Style)FindResource("NavButton"),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                button.Click += (_, _) => action.Run();
                rows.Children.Add(button);
            }
            if (rows.Children.Count == 0)
                rows.Children.Add(new TextBlock { Text = "No hay acciones coincidentes." });
        }

        query.TextChanged += (_, _) => Render();
        query.PreviewKeyDown += (_, key) =>
        {
            if (key.Key == System.Windows.Input.Key.Enter && rows.Children.OfType<Button>().FirstOrDefault() is { } first)
            {
                key.Handled = true;
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        Render();
        ShowPage(rootLayoutPalette);
        query.Focus();
    }
}
