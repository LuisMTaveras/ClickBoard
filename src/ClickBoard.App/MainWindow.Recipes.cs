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
        try { loaded = TransformationRecipes.Load().Where(recipe => string.Equals(recipe.Project, project, StringComparison.OrdinalIgnoreCase)).ToList(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { _viewModel.StatusMessage = "No se pudieron leer las recetas guardadas. El archivo se conserva."; return; }
        var recipes = new ObservableCollection<TransformationRecipe>(loaded);
        void SaveScoped(IReadOnlyList<TransformationRecipe> updated)
        {
            var all = TransformationRecipes.Load().Where(recipe => !string.Equals(recipe.Project, project, StringComparison.OrdinalIgnoreCase)).ToList();
            all.AddRange(updated); TransformationRecipes.Save(all);
        }
        var steps = new ObservableCollection<string>();
        var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 650, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "Recetas · " + (project ?? "Generales"), FontSize = 22, Margin = new Thickness(0,0,0,12) });
        panel.Children.Add(new TextBlock { Text = "Se aplican al texto de la vista previa en el orden indicado. Deshacer revierte la receta completa.", TextWrapping = TextWrapping.Wrap });
        var saved = new ComboBox { ItemsSource = recipes, DisplayMemberPath = "Name", Margin = new Thickness(0,12,0,8), MinWidth = 400 };
        panel.Children.Add(saved);
        panel.Children.Add(new TextBlock { Text = "Nombre", Margin = new Thickness(0,8,0,5) });
        var name = new TextBox { MaxLength = 80, Style = (Style)FindResource("SearchBox") }; panel.Children.Add(name);
        var actions = new ComboBox { ItemsSource = TransformationRecipes.Actions, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedIndex = 0, Margin = new Thickness(0,12,0,8) }; panel.Children.Add(actions);
        var list = new ListBox { Height = 180, Margin = new Thickness(0,8,0,8) }; panel.Children.Add(list);
        steps.CollectionChanged += (_, _) =>
        {
            var selection = list.SelectedIndex;
            list.ItemsSource = steps.Select((key, index) => $"{index + 1}. {TransformationRecipes.Actions[key]}").ToArray();
            list.SelectedIndex = Math.Min(selection, steps.Count - 1);
        };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) };
        var bar = new WrapPanel(); panel.Children.Add(bar);
        void AddButton(string title, Action action)
        {
            var button = new Button { Content = title, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0,0,6,6) };
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { feedback.Text = "No se pudo completar la operación. Los cambios no guardados permanecen en el editor."; }
            };
            bar.Children.Add(button);
        }
        AddButton("Añadir paso", () => { if (steps.Count < 20 && actions.SelectedValue is string key) steps.Add(key); else feedback.Text = "Máximo 20 pasos."; });
        AddButton("Quitar paso", () => { if (list.SelectedIndex >= 0) steps.RemoveAt(list.SelectedIndex); });
        void Move(int delta) { var index = list.SelectedIndex; var next = index + delta; if (index >= 0 && next >= 0 && next < steps.Count) { steps.Move(index, next); list.SelectedIndex = next; } }
        AddButton("Subir", () => Move(-1)); AddButton("Bajar", () => Move(1));
        AddButton("Nueva", () => { saved.SelectedIndex = -1; name.Clear(); steps.Clear(); });
        AddButton("Guardar receta", () =>
        {
            var title = name.Text.Trim();
            if (title.Length == 0 || steps.Count == 0) { feedback.Text = "Escribe un nombre y añade al menos un paso."; return; }
            var replacement = new TransformationRecipe(title, steps.ToArray(), project);
            var updated = recipes.ToList(); var existing = updated.FindIndex(recipe => recipe.Name.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                if (System.Windows.MessageBox.Show(this, $"¿Reemplazar la receta «{title}»?", "Guardar receta", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                updated[existing] = replacement;
            }
            else updated.Add(replacement);
            SaveScoped(updated);
            recipes.Clear(); foreach (var recipe in updated) recipes.Add(recipe);
            saved.SelectedItem = replacement; feedback.Text = "Receta guardada.";
        });
        AddButton("Eliminar receta", () =>
        {
            if (saved.SelectedItem is not TransformationRecipe selected) return;
            if (System.Windows.MessageBox.Show(this, $"¿Eliminar la receta «{selected.Name}»?", "Eliminar receta", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var updated = recipes.Where(recipe => recipe != selected).ToList(); SaveScoped(updated); recipes.Remove(selected); feedback.Text = "Receta eliminada.";
        });
        AddButton("Aplicar y ver resultado", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || steps.Count == 0) { feedback.Text = "Añade nombre y pasos para aplicar la receta."; return; }
            if (_viewModel.ApplyRecipe(new(name.Text.Trim(), steps.ToArray()))) ClosePage(); else feedback.Text = _viewModel.StatusMessage;
        });
        AddButton("Volver", ClosePage);
        panel.Children.Add(feedback);
        saved.SelectionChanged += (_, _) => { if (saved.SelectedItem is TransformationRecipe recipe) { name.Text = recipe.Name; steps.Clear(); foreach (var step in recipe.Steps) steps.Add(step); } };
        ShowPage(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}
