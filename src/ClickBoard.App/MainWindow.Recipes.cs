using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ClickBoard.App.Settings;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace ClickBoard.App;

public partial class MainWindow
{
    private void OpenRecipes_Click(object sender, RoutedEventArgs e)
    {
        List<TransformationRecipe> loaded;
        var project = _viewModel.ActiveProject;
        static bool InScope(TransformationRecipe recipe, string? activeProject) =>
            recipe.Project is null || string.Equals(recipe.Project, activeProject, StringComparison.OrdinalIgnoreCase);

        try
        {
            loaded = TransformationRecipes.Load().Where(recipe => InScope(recipe, project)).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _viewModel.StatusMessage = "No se pudieron leer las recetas guardadas. El archivo se conserva.";
            return;
        }
        var recipes = new ObservableCollection<TransformationRecipe>(loaded);
        void SaveScoped(IReadOnlyList<TransformationRecipe> updated)
        {
            var all = TransformationRecipes.Load().Where(recipe => !InScope(recipe, project)).ToList();
            all.AddRange(updated);
            TransformationRecipes.Save(all);
        }
        var steps = new ObservableCollection<string>();
        var (rootLayout, page, _) = CreateSubpageLayout(
            "Recetas",
            "Recetas de transformación" + (project is not null ? $" · {project}" : ""),
            "Encadena transformaciones secuenciales para procesar textos en un clic. Puedes asignar recetas al proyecto actual o de uso general.");

        // Tarjeta 1: Recetas Guardadas y Acciones Principales
        var manageCard = new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 14) };
        var managePanel = new StackPanel();
        manageCard.Child = managePanel;

        managePanel.Children.Add(new TextBlock { Text = "Receta seleccionada", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var saved = new ComboBox { ItemsSource = recipes, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 0, 12) };
        managePanel.Children.Add(saved);

        var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBox { MaxLength = 80, Style = (Style)FindResource("SearchBox"), ToolTip = "Nombre de la receta", Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(name, 0);
        nameGrid.Children.Add(name);

        var saveBtn = new Button { Content = "Guardar", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(saveBtn, 1);
        nameGrid.Children.Add(saveBtn);

        var delBtn = new Button { Content = "Eliminar", Style = (Style)FindResource("SecondaryButton") };
        Grid.SetColumn(delBtn, 2);
        nameGrid.Children.Add(delBtn);
        managePanel.Children.Add(nameGrid);

        page.Children.Add(manageCard);

        // Tarjeta 2: Pasos de la receta
        var stepsCard = new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 14) };
        var stepsPanel = new StackPanel();
        stepsCard.Child = stepsPanel;

        var stepsHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        stepsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stepsTitle = new TextBlock { Text = "Pasos en secuencia", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stepsTitle, 0);
        stepsHeader.Children.Add(stepsTitle);

        var stepControls = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var upBtn = new Button { Content = "▲", Style = (Style)FindResource("ToolbarButton"), ToolTip = "Subir paso", Margin = new Thickness(0, 0, 4, 0), Width = 32 };
        var downBtn = new Button { Content = "▼", Style = (Style)FindResource("ToolbarButton"), ToolTip = "Bajar paso", Margin = new Thickness(0, 0, 4, 0), Width = 32 };
        var removeStepBtn = new Button { Content = "Quitar", Style = (Style)FindResource("ToolbarButton"), ToolTip = "Quitar paso seleccionado" };
        stepControls.Children.Add(upBtn);
        stepControls.Children.Add(downBtn);
        stepControls.Children.Add(removeStepBtn);
        Grid.SetColumn(stepControls, 1);
        stepsHeader.Children.Add(stepControls);
        stepsPanel.Children.Add(stepsHeader);

        var list = CreateThemedListBox(new Thickness(0, 0, 0, 12), height: 150);
        stepsPanel.Children.Add(list);
        steps.CollectionChanged += (_, _) =>
        {
            var selection = list.SelectedIndex;
            list.ItemsSource = steps.Select((key, index) => $"{index + 1}. {TransformationRecipes.Actions[key]}").ToArray();
            list.SelectedIndex = Math.Min(selection, steps.Count - 1);
        };

        var addStepGrid = new Grid();
        addStepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addStepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new ComboBox { ItemsSource = TransformationRecipes.Actions, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(actions, 0);
        addStepGrid.Children.Add(actions);
        var addStepBtn = new Button { Content = "Añadir paso", Style = (Style)FindResource("SecondaryButton") };
        Grid.SetColumn(addStepBtn, 1);
        addStepGrid.Children.Add(addStepBtn);
        stepsPanel.Children.Add(addStepGrid);

        page.Children.Add(stepsCard);

        var feedback = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        feedback.SetResourceReference(ForegroundProperty, "MutedBrush");

        // Eventos
        addStepBtn.Click += (_, _) => { if (steps.Count < 20 && actions.SelectedValue is string key) steps.Add(key); else feedback.Text = "Máximo 20 pasos."; };
        removeStepBtn.Click += (_, _) => { if (list.SelectedIndex >= 0) steps.RemoveAt(list.SelectedIndex); };
        void Move(int delta) { var index = list.SelectedIndex; var next = index + delta; if (index >= 0 && next >= 0 && next < steps.Count) { steps.Move(index, next); list.SelectedIndex = next; } }
        upBtn.Click += (_, _) => Move(-1);
        downBtn.Click += (_, _) => Move(1);

        saveBtn.Click += (_, _) =>
        {
            var title = name.Text.Trim();
            if (title.Length == 0 || steps.Count == 0) { feedback.Text = "Escribe un nombre y añade al menos un paso."; return; }
            var replacement = new TransformationRecipe(title, steps.ToArray(), project);
            var updated = recipes.ToList(); var existing = updated.FindIndex(recipe => recipe.Name.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                if (!ModernDialogWindow.ShowConfirm(this,
                    $"¿Reemplazar la receta existente «{title}»?",
                    title: "Guardar receta",
                    confirmText: "Reemplazar",
                    cancelText: "Cancelar",
                    type: ModernDialogType.Warning)) return;
                updated[existing] = replacement;
            }
            else updated.Add(replacement);
            SaveScoped(updated);
            recipes.Clear(); foreach (var recipe in updated) recipes.Add(recipe);
            saved.SelectedItem = replacement; feedback.Text = "Receta guardada.";
        };

        delBtn.Click += (_, _) =>
        {
            if (saved.SelectedItem is not TransformationRecipe selected) return;
            if (!ModernDialogWindow.ShowConfirm(this,
                $"¿Eliminar la receta «{selected.Name}»?",
                title: "Eliminar receta",
                confirmText: "Eliminar",
                cancelText: "Cancelar",
                type: ModernDialogType.Danger)) return;
            var updated = recipes.Where(recipe => recipe != selected).ToList(); SaveScoped(updated); recipes.Remove(selected); feedback.Text = "Receta eliminada.";
        };

        // Pie
        var bottomGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var backBtn = new Button { Content = "←  Volver al historial", Style = (Style)FindResource("SecondaryButton") };
        backBtn.Click += (_, _) => ClosePage();
        Grid.SetColumn(backBtn, 0);
        bottomGrid.Children.Add(backBtn);

        var applyBtn = new Button { Content = "Aplicar receta al clip seleccionado", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(16, 6, 16, 6) };
        applyBtn.Click += (_, _) =>
        {
            if (steps.Count == 0) { feedback.Text = "Añade al menos un paso para aplicar."; return; }
            var recipeName = string.IsNullOrWhiteSpace(name.Text) ? "Receta" : name.Text.Trim();
            if (_viewModel.ApplyRecipe(new(recipeName, steps.ToArray()))) ClosePage();
            else feedback.Text = _viewModel.StatusMessage;
        };
        Grid.SetColumn(applyBtn, 2);
        bottomGrid.Children.Add(applyBtn);

        page.Children.Add(feedback);
        page.Children.Add(bottomGrid);

        saved.SelectionChanged += (_, _) => { if (saved.SelectedItem is TransformationRecipe recipe) { name.Text = recipe.Name; steps.Clear(); foreach (var step in recipe.Steps) steps.Add(step); } };
        ShowPage(rootLayout);
    }
}
