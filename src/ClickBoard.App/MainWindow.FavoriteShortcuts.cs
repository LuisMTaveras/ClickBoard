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
    private void OpenFavoriteShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedClip;
        var (rootLayout, _, panel) = CreateSubpageLayout(
            "Atajos",
            "Atajos globales de favoritos",
            selected?.IsFavorite == true
                ? $"Asignar combinación rápida a: «{selected.DisplayTitle}»"
                : "Selecciona un favorito en el historial para asignarle un atajo directo, o administra tus atajos activos desde esta lista.");
        var list = CreateThemedListBox(height: 200);
        list.DisplayMemberPath = "Value";
        panel.Children.Add(list);
        async Task RefreshAsync()
        {
            var bindings = GetFavoriteShortcuts?.Invoke() ?? [];
            var items = new List<KeyValuePair<long, string>>(bindings.Count);
            foreach (var pair in bindings)
            {
                var clip = _viewModel.Clips.FirstOrDefault(c => c.Id == pair.Key) ?? await _viewModel.LoadClipAsync(pair.Key);
                var label = clip is not null ? clip.ShortPreview : $"clip #{pair.Key}";
                items.Add(new KeyValuePair<long, string>(pair.Key, $"{pair.Value}  ·  {label}"));
            }
            list.ItemsSource = items;
        }
        _ = RefreshAsync();
        panel.Children.Add(new TextBlock { Text = "Combinación de teclas (ej. Ctrl+Alt+1)", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 5) });
        var box = new TextBox
        {
            Text = selected is null ? "" : GetFavoriteShortcuts?.Invoke().GetValueOrDefault(selected.Id) ?? "",
            ToolTip = "Ejemplo: Ctrl+Alt+1",
            Style = (Style)FindResource("SearchBox"),
            MaxLength = 100,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(box);
        var status = new TextBlock
        {
            Text = "Incluye Ctrl, Alt o Mayús y una tecla. Windows comprueba si la combinación está ocupada.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        status.SetResourceReference(ForegroundProperty, "MutedBrush");
        panel.Children.Add(status);
        var bar = new WrapPanel();
        var assign = new Button
        {
            Content = "Asignar al favorito seleccionado",
            IsEnabled = selected?.IsFavorite == true && selected.Id > 0,
            Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        assign.Click += async (_, _) =>
        {
            if (selected is null || selected.Id <= 0 || string.IsNullOrWhiteSpace(box.Text))
            {
                status.Text = selected is { Id: <= 0 }
                    ? "Los clips del modo temporal no admiten atajos permanentes."
                    : "Introduce un atajo.";
                return;
            }
            status.Text = SetFavoriteShortcut?.Invoke(selected.Id, box.Text) ?? (SetFavoriteShortcut is null ? "Servicio no disponible." : "Atajo guardado y activo.");
            await RefreshAsync();
        };
        var remove = new Button
        {
            Content = "Quitar atajo seleccionado",
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedItem is KeyValuePair<long, string> pair)
            {
                status.Text = SetFavoriteShortcut?.Invoke(pair.Key, "") ?? "Atajo quitado.";
                await RefreshAsync();
            }
        };
        var back = new Button { Content = "←  Volver", Style = (Style)FindResource("SecondaryButton") };
        back.Click += (_, _) => ClosePage();
        bar.Children.Add(assign);
        bar.Children.Add(remove);
        bar.Children.Add(back);
        panel.Children.Add(bar);
        ShowPage(rootLayout);
    }
}
