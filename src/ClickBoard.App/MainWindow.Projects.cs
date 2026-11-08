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
            var (rootLayout, page, _) = CreateSubpageLayout(
                "Proyectos",
                "Espacios por proyecto",
                $"{selectedIds.Length} clips seleccionados en el historial. El proyecto activo filtra automáticamente Favoritos, Imágenes y Desarrollo.");

            // 1. Tarjeta: Proyecto Activo y Creación
            var activeCard = new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 14) };
            var activePanel = new StackPanel();
            activeCard.Child = activePanel;

            var activeHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            activeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            activeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var activeTitleStack = new StackPanel();
            activeTitleStack.Children.Add(new TextBlock { Text = "Espacio de trabajo actual", FontSize = 15, FontWeight = FontWeights.SemiBold });
            var curText = new TextBlock
            {
                Text = _viewModel.ActiveProject is null ? "Mostrando todos los proyectos (vista global)" : $"Proyecto activo: {_viewModel.ActiveProject}",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            };
            curText.SetResourceReference(ForegroundProperty, "AccentBrush");
            activeTitleStack.Children.Add(curText);
            Grid.SetColumn(activeTitleStack, 0);
            activeHeader.Children.Add(activeTitleStack);

            var viewAllBtn = new Button { Content = "Ver todos", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(14, 5, 14, 5) };
            viewAllBtn.Click += async (_, _) => { await _viewModel.SelectProjectAsync(null); ProjectChanged?.Invoke(null); ClosePage(); };
            Grid.SetColumn(viewAllBtn, 1);
            activeHeader.Children.Add(viewAllBtn);
            activePanel.Children.Add(activeHeader);

            var list = CreateThemedListBox(new Thickness(0, 0, 0, 12), height: 160);
            list.ItemsSource = names;
            list.SelectedItem = _viewModel.ActiveProject;
            activePanel.Children.Add(list);

            var projectActionsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            projectActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            projectActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var openProjectBtn = new Button { Content = "Abrir proyecto seleccionado", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
            openProjectBtn.Click += async (_, _) =>
            {
                if (list.SelectedItem is not string project) return;
                await _viewModel.SelectProjectAsync(project); ProjectChanged?.Invoke(project); ClosePage();
            };
            Grid.SetColumn(openProjectBtn, 0);
            projectActionsGrid.Children.Add(openProjectBtn);

            var moveSelectionBtn = new Button { Content = "Mover selección aquí", Style = (Style)FindResource("SecondaryButton") };
            moveSelectionBtn.Click += async (_, _) =>
            {
                if (selectedIds.Length == 0 || list.SelectedItem is not string project) return;
                await _viewModel.MoveToProjectAsync(selectedIds, project); ClipTitleChanged?.Invoke(this, EventArgs.Empty);
                curText.Text = $"{selectedIds.Length} clips movidos a {project}.";
            };
            Grid.SetColumn(moveSelectionBtn, 1);
            projectActionsGrid.Children.Add(moveSelectionBtn);
            activePanel.Children.Add(projectActionsGrid);

            // Fila para crear nuevo proyecto
            var createGrid = new Grid();
            createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameBox = new TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 80, ToolTip = "Nombre del nuevo proyecto (ej. Cliente Acme)", Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(nameBox, 0);
            createGrid.Children.Add(nameBox);
            var createBtn = new Button { Content = "Crear proyecto", Style = (Style)FindResource("SecondaryButton"), Padding = new Thickness(14, 5, 14, 5) };
            createBtn.Click += async (_, _) =>
            {
                var title = nameBox.Text.Trim(); if (title.Length == 0) return;
                await _viewModel.CreateProjectAsync(title); list.ItemsSource = await _viewModel.GetProjectsAsync(); list.SelectedItem = title; nameBox.Clear();
            };
            Grid.SetColumn(createBtn, 1);
            createGrid.Children.Add(createBtn);
            activePanel.Children.Add(createGrid);

            page.Children.Add(activeCard);

            // 2. Tarjeta: Detección Automática de Reglas
            var rulesCard = new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 14) };
            var rulesPanel = new StackPanel();
            rulesCard.Child = rulesPanel;

            var rulesHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            rulesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rulesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rulesTitleStack = new StackPanel();
            rulesTitleStack.Children.Add(new TextBlock { Text = "Detección inteligente de proyecto", FontSize = 15, FontWeight = FontWeights.SemiBold });
            var rulesSub = new TextBlock { Text = "Cambia el proyecto activo automáticamente según la ventana y el archivo en primer plano.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            rulesSub.SetResourceReference(ForegroundProperty, "MutedBrush");
            rulesTitleStack.Children.Add(rulesSub);
            Grid.SetColumn(rulesTitleStack, 0);
            rulesHeader.Children.Add(rulesTitleStack);

            var autoToggle = new System.Windows.Controls.CheckBox
            {
                Content = "Activar",
                IsChecked = ProjectDetection.Current.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            autoToggle.Click += (_, _) =>
            {
                try { ProjectDetection.Save(ProjectDetection.Current with { Enabled = autoToggle.IsChecked == true }); }
                catch { autoToggle.IsChecked = ProjectDetection.Current.Enabled; }
            };
            Grid.SetColumn(autoToggle, 1);
            rulesHeader.Children.Add(autoToggle);
            rulesPanel.Children.Add(rulesHeader);

            var rulesList = CreateThemedListBox(new Thickness(0, 0, 0, 10), height: 110);
            rulesList.DisplayMemberPath = "Value";
            rulesPanel.Children.Add(rulesList);
            void RefreshRules() => rulesList.ItemsSource = ProjectDetection.Current.Rules
                .Select(rule => new KeyValuePair<ProjectRule, string>(rule, $"{rule.Project}  ◀  {rule.Process} ({rule.TitleContains})"))
                .ToArray();
            RefreshRules();

            var ruleFormGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            ruleFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var procBox = new TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 80, ToolTip = "Proceso (ej. Code o devenv)" };
            Grid.SetColumn(procBox, 0);
            ruleFormGrid.Children.Add(procBox);

            var titleBox = new TextBox { Style = (Style)FindResource("SearchBox"), MaxLength = 120, ToolTip = "Título de ventana contiene (ej. MiApp)" };
            Grid.SetColumn(titleBox, 2);
            ruleFormGrid.Children.Add(titleBox);

            var addRuleBtn = new Button { Content = "Añadir regla", Style = (Style)FindResource("SecondaryButton") };
            addRuleBtn.Click += (_, _) =>
            {
                if (list.SelectedItem is not string project || string.IsNullOrWhiteSpace(procBox.Text) || string.IsNullOrWhiteSpace(titleBox.Text)) return;
                var rule = new ProjectRule(project, procBox.Text.Trim(), titleBox.Text.Trim());
                var updated = ProjectDetection.Current.Rules.ToList();
                if (!updated.Contains(rule)) updated.Add(rule);
                ProjectDetection.Save(ProjectDetection.Current with { Rules = updated });
                RefreshRules();
                procBox.Clear(); titleBox.Clear();
            };
            Grid.SetColumn(addRuleBtn, 4);
            ruleFormGrid.Children.Add(addRuleBtn);

            var delRuleBtn = new Button { Content = "Eliminar", Style = (Style)FindResource("SecondaryButton") };
            delRuleBtn.Click += (_, _) =>
            {
                if (rulesList.SelectedItem is KeyValuePair<ProjectRule, string> selected)
                {
                    ProjectDetection.Save(ProjectDetection.Current with { Rules = ProjectDetection.Current.Rules.Where(r => r != selected.Key).ToList() });
                    RefreshRules();
                }
            };
            Grid.SetColumn(delRuleBtn, 6);
            ruleFormGrid.Children.Add(delRuleBtn);

            rulesPanel.Children.Add(ruleFormGrid);
            page.Children.Add(rulesCard);

            // Pie: Botón Volver alineado a la izquierda
            var backBtn = new Button { Content = "←  Volver al historial", Style = (Style)FindResource("SecondaryButton"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            backBtn.Click += (_, _) => ClosePage();
            page.Children.Add(backBtn);

            ShowPage(rootLayout);
        }
        catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
        { _viewModel.StatusMessage = "No se pudieron cargar los proyectos."; }
    }
}
