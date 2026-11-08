using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClickBoard.Core.Models;
using ClickBoard.App.Settings;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

namespace ClickBoard.App;

public partial class MainWindow
{
    public event Action<string?>? ProjectChanged;
    private async void OpenProjects_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = ClipList.SelectedItems.OfType<ClipboardClip>().Select(clip => clip.Id).ToArray();
        try
        {
            var names = await _viewModel.GetProjectsAsync();
            var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 650, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            panel.Children.Add(new TextBlock { Text = "Espacios por proyecto", FontSize = 22, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(new TextBlock { Text = $"{selectedIds.Length} clips seleccionados para mover. El proyecto filtra también Favoritos, Imágenes y Desarrollo.", TextWrapping = TextWrapping.Wrap });
            var list = CreateThemedListBox(new Thickness(0, 12, 0, 12), 240);
            list.ItemsSource = names;
            list.SelectedItem = _viewModel.ActiveProject;
            list.MinWidth = 400;
            panel.Children.Add(list);
            var name = new TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 80, ToolTip = "Nombre del nuevo proyecto" };
            panel.Children.Add(name);
            panel.Children.Add(new TextBlock { Text = "Detección automática", FontSize = 17, Margin = new Thickness(0, 20, 0, 8) });
            panel.Children.Add(new TextBlock { Text = "Cada regla combina proceso y un fragmento del título. Ejemplo: Code + API-SanJuan. Seleccionar un proyecto manualmente desactiva la detección. Las nuevas capturas no se asignan automáticamente.", TextWrapping = TextWrapping.Wrap });
            var automatic = new System.Windows.Controls.CheckBox
            {
                Content = "Detectar el proyecto por la ventana activa",
                IsChecked = ProjectDetection.Current.Enabled,
                Margin = new Thickness(0, 10, 0, 10)
            };
            automatic.SetResourceReference(ForegroundProperty, "TextBrush");
            panel.Children.Add(automatic);
            panel.Children.Add(new TextBlock { Text = "Proceso (ejemplo: Code o devenv)", Margin = new Thickness(0, 6, 0, 5) });
            var process = new TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 100 };
            panel.Children.Add(process);
            panel.Children.Add(new TextBlock { Text = "El título debe contener (ejemplo: API-SanJuan)", Margin = new Thickness(0, 6, 0, 5) });
            var titleMatch = new TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 200 };
            panel.Children.Add(titleMatch);
            var rules = CreateThemedListBox(new Thickness(0, 10, 0, 8), 120);
            rules.DisplayMemberPath = "Value";
            panel.Children.Add(rules);
            void RefreshRules() => rules.ItemsSource = ProjectDetection.Current.Rules
                .Select(rule => new KeyValuePair<ProjectRule, string>(rule, $"{rule.Project}: {rule.Process} · {rule.TitleContains}"))
                .ToArray();
            RefreshRules();
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
            automatic.Click += (_, _) =>
            {
                try { ProjectDetection.Save(ProjectDetection.Current with { Enabled = automatic.IsChecked == true }); feedback.Text = "Preferencia aplicada."; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { automatic.IsChecked = ProjectDetection.Current.Enabled; feedback.Text = "No se pudo guardar la preferencia."; }
            };
            var buttons = new WrapPanel { Margin = new Thickness(0,12,0,0) }; panel.Children.Add(buttons);
            void Button(string label, Func<Task> action)
            {
                var button = new Button { Content = label, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0,0,6,6) };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try { await action(); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or ArgumentException) { feedback.Text = "No se pudo guardar el cambio de proyecto."; }
                    finally { button.IsEnabled = true; }
                };
                buttons.Children.Add(button);
            }
            Button("Crear proyecto", async () =>
            {
                var title = name.Text.Trim(); if (title.Length == 0) { feedback.Text = "Escribe un nombre."; return; }
                await _viewModel.CreateProjectAsync(title); list.ItemsSource = await _viewModel.GetProjectsAsync(); list.SelectedItem = title; name.Clear(); feedback.Text = "Proyecto creado.";
            });
            Button("Añadir regla al proyecto seleccionado", () =>
            {
                if (list.SelectedItem is not string project || string.IsNullOrWhiteSpace(process.Text) || string.IsNullOrWhiteSpace(titleMatch.Text))
                { feedback.Text = "Selecciona un proyecto y completa proceso y título."; return Task.CompletedTask; }
                var rule = new ProjectRule(project, process.Text.Trim(), titleMatch.Text.Trim());
                var updated = ProjectDetection.Current.Rules.ToList();
                if (!updated.Contains(rule)) updated.Add(rule);
                ProjectDetection.Save(ProjectDetection.Current with { Rules = updated }); RefreshRules(); feedback.Text = "Regla guardada. Activa la detección para usarla."; return Task.CompletedTask;
            });
            Button("Eliminar regla seleccionada", () =>
            {
                if (rules.SelectedItem is KeyValuePair<ProjectRule,string> selected)
                { ProjectDetection.Save(ProjectDetection.Current with { Rules = ProjectDetection.Current.Rules.Where(rule => rule != selected.Key).ToList() }); RefreshRules(); feedback.Text = "Regla eliminada."; }
                return Task.CompletedTask;
            });
            Button("Abrir proyecto", async () =>
            {
                if (list.SelectedItem is not string project) { feedback.Text = "Selecciona un proyecto."; return; }
                await _viewModel.SelectProjectAsync(project); ProjectChanged?.Invoke(project); ClosePage();
            });
            Button("Ver todos", async () => { await _viewModel.SelectProjectAsync(null); ProjectChanged?.Invoke(null); ClosePage(); });
            Button("Mover selección aquí", async () =>
            {
                if (selectedIds.Length == 0 || list.SelectedItem is not string project) { feedback.Text = "Selecciona clips en el historial y elige el proyecto de destino."; return; }
                await _viewModel.MoveToProjectAsync(selectedIds, project); ClipTitleChanged?.Invoke(this, EventArgs.Empty); feedback.Text = $"{selectedIds.Length} clips movidos a {project}.";
            });
            Button("Quitar proyecto de selección", async () =>
            {
                if (selectedIds.Length == 0) { feedback.Text = "No hay clips seleccionados."; return; }
                await _viewModel.MoveToProjectAsync(selectedIds, null); ClipTitleChanged?.Invoke(this, EventArgs.Empty); feedback.Text = "Asignación de proyecto retirada.";
            });
            Button("Volver", () => { ClosePage(); return Task.CompletedTask; });
            panel.Children.Add(feedback);
            ShowPage(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
        { _viewModel.StatusMessage = "No se pudieron cargar los proyectos."; }
    }
}
