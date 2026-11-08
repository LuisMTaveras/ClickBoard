using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

namespace ClickBoard.App;

public partial class MainWindow
{
    internal Func<Dictionary<long,string>>? GetFavoriteShortcuts { get; set; }
    internal Func<long,string,string?>? SetFavoriteShortcut { get; set; }
    private void OpenFavoriteShortcuts_Click(object sender,RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedClip;
        var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 650 };
        panel.Children.Add(new TextBlock { Text = "Atajos de favoritos", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = selected?.IsFavorite == true ? $"Asignar a: {selected.DisplayTitle}" : "Selecciona un favorito en el historial para asignarle un atajo. Puedes quitar atajos existentes desde esta lista.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        var list = CreateThemedListBox(height: 200);
        list.DisplayMemberPath = "Value";
        panel.Children.Add(list);
        void Refresh() => list.ItemsSource = (GetFavoriteShortcuts?.Invoke() ?? [])
            .Select(pair => new KeyValuePair<long, string>(pair.Key, $"{pair.Value} · clip {pair.Key}"))
            .ToArray();
        Refresh();
        var box = new TextBox
        {
            Text = selected is null ? "" : GetFavoriteShortcuts?.Invoke().GetValueOrDefault(selected.Id) ?? "",
            ToolTip = "Ejemplo: Ctrl+Alt+1",
            Style = (Style)FindResource("SearchBox"),
            MaxLength = 100,
            Margin = new Thickness(0, 12, 0, 12)
        };
        panel.Children.Add(box);
        var status = new TextBlock
        {
            Text = "Incluye Ctrl, Alt o Mayús y una tecla. Windows comprueba si la combinación está ocupada.",
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(status);
        var assign = new Button
        {
            Content = "Asignar al favorito seleccionado",
            IsEnabled = selected?.IsFavorite == true,
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(0, 10, 0, 6)
        };
        panel.Children.Add(assign);
        assign.Click += (_, _) =>
        {
            if (selected is null || string.IsNullOrWhiteSpace(box.Text))
            {
                status.Text = "Introduce un atajo.";
                return;
            }
            status.Text = SetFavoriteShortcut?.Invoke(selected.Id, box.Text) ?? (SetFavoriteShortcut is null ? "Servicio no disponible." : "Atajo guardado y activo.");
            Refresh();
        };
        var remove = new Button
        {
            Content = "Quitar atajo de la lista",
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(0, 6, 0, 6)
        };
        panel.Children.Add(remove);
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is KeyValuePair<long, string> pair)
            {
                status.Text = SetFavoriteShortcut?.Invoke(pair.Key, "") ?? "Atajo quitado.";
                Refresh();
            }
        };
        var back = new Button { Content = "Volver", Style = (Style)FindResource("SecondaryButton") };
        panel.Children.Add(back);
        back.Click += (_, _) => ClosePage();
        ShowPage(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}
