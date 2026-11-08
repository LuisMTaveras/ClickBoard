using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClickBoard.App.Settings;
using ClickBoard.App.ViewModels;
using ClickBoard.Core.Models;

namespace ClickBoard.App;

public partial class QuickPasteWindow : Window
{
    private readonly ClipboardViewModel _viewModel;
    private ClickBoardSettings _settings;

    public QuickPasteWindow(ClipboardViewModel viewModel, ClickBoardSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;
        UpdateSettings(settings);
        Deactivated += (_, _) => { if (_settings.CloseQuickOnFocusLoss) Hide(); };
        Loaded += (_, _) => FocusSearch();
    }

    public void UpdateSettings(ClickBoardSettings settings)
    {
        _settings = settings;
        var primary = settings.QuickEnterAction == QuickPasteAction.PlainText ? "Texto plano" : "Pegar";
        var hints = new List<string> { $"Enter {primary}" };
        if (settings.EnableAlternatePasteAction)
            hints.Add(settings.QuickEnterAction == QuickPasteAction.PlainText ? "Shift+Enter Pegar" : "Shift+Enter Texto plano");
        if (settings.EnableFavoriteQuickAction)
            hints.Add("Ctrl+F Favorito");
        hints.Add("Esc Cerrar");
        QuickActionHint.Text = string.Join(" · ", hints);
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        if (ClipList.Items.Count > 0 && ClipList.SelectedIndex < 0)
            ClipList.SelectedIndex = 0;
    }

    private async void ClipList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 3)
            await _viewModel.LoadMoreAsync();
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(SearchBox.Text))
                SearchBox.Clear();
            else
                Hide();
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (ClipList.Items.Count > 0)
                ClipList.SelectedIndex = Math.Min(ClipList.Items.Count - 1, ClipList.SelectedIndex + 1);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (ClipList.Items.Count > 0)
                ClipList.SelectedIndex = Math.Max(0, ClipList.SelectedIndex - 1);
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _settings.EnableFavoriteQuickAction)
        {
            e.Handled = true;
            await _viewModel.ToggleFavoriteAsync(_viewModel.SelectedClip);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var alternate = Keyboard.Modifiers == ModifierKeys.Shift;
            if (!alternate || _settings.EnableAlternatePasteAction)
                await PasteQuickActionAsync(alternate);
        }
    }

    private async Task PasteQuickActionAsync(bool alternate)
    {
        if (_viewModel.SelectedClip is null)
            return;
        var action = _settings.QuickEnterAction;
        if (alternate)
            action = action == QuickPasteAction.PlainText ? QuickPasteAction.PreserveFormatting : QuickPasteAction.PlainText;

        if (action == QuickPasteAction.PlainText && _viewModel.CanPastePlainText)
            await _viewModel.PasteSelectedPlainTextAsync();
        else
            await _viewModel.PasteSelectedAsync();
        if (_viewModel.LastPasteSent)
            Hide();
        else
            QuickActionHint.Text = _viewModel.StatusMessage;
    }

    private async void ClipList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(ClipList, source) is not ListBoxItem item)
            return;
        ClipList.SelectedItem = item.DataContext;
        await PasteQuickActionAsync(alternate: false);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
}
