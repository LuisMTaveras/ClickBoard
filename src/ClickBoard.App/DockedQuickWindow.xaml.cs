using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClickBoard.App.Settings;
using ClickBoard.App.ViewModels;
using ClickBoard.Core.Models;

namespace ClickBoard.App;

public partial class DockedQuickWindow : Window
{
    private readonly ClipboardViewModel _viewModel;
    private ClickBoardSettings _settings;

    public DockedQuickWindow(ClipboardViewModel viewModel, ClickBoardSettings settings)
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
        DockedShortcutHint.Text = settings.DockedPanelHotkey;
        var primary = settings.QuickEnterAction == QuickPasteAction.PlainText ? "Enter Texto plano" : "Enter Pegar";
        var hints = new List<string> { primary };
        if (settings.EnableAlternatePasteAction)
            hints.Add(settings.QuickEnterAction == QuickPasteAction.PlainText ? "Shift+Enter Pegar" : "Shift+Enter Plano");
        if (settings.EnableFavoriteQuickAction)
            hints.Add("Ctrl+F Favorito");
        DockedActionHint.Text = string.Join(" · ", hints);
    }

    public void PlaceAtScreenEdge()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, area.Left + 12, area.Top + 12, 0, 0, 0x0001 | 0x0004 | 0x0010);
        var dpi = GetDpiForWindow(handle);
        var scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var dipMaxWidth = Math.Max(100, (area.Width - 24) / scale);
        var dipMaxHeight = Math.Max(100, (area.Height - 24) / scale);
        MinHeight = Math.Min(560, dipMaxHeight);
        MinWidth = Math.Min(430, dipMaxWidth);
        var targetDipWidth = Math.Min(double.IsNaN(Width) || Width <= 0 ? 490 : Width, dipMaxWidth);
        var targetDipHeight = Math.Min(820, dipMaxHeight);
        Width = targetDipWidth;
        Height = targetDipHeight;
        var pixelWidth = (int)Math.Round(targetDipWidth * scale);
        var pixelHeight = (int)Math.Round(targetDipHeight * scale);
        var pixelX = area.Right - pixelWidth - 12;
        var pixelY = area.Top + 12;
        Left = pixelX / scale;
        Top = pixelY / scale;
        SetWindowPos(handle, IntPtr.Zero, pixelX, pixelY, pixelWidth, pixelHeight, 0x0004 | 0x0010);
    }

    public void ResetFilterSelection() => SetActiveFilter(AllFilterButton);

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

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

    private async void All_Click(object sender, RoutedEventArgs e) => await ApplyFilterAsync(null);
    private async void Text_Click(object sender, RoutedEventArgs e) => await ApplyFilterAsync(ClipKind.Text);
    private async void Images_Click(object sender, RoutedEventArgs e) => await ApplyFilterAsync(ClipKind.Image);

    private async void Developer_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SetDeveloperFilterAsync();
        SetActiveFilter(DeveloperFilterButton);
    }

    private async Task ApplyFilterAsync(ClipKind? kind)
    {
        await _viewModel.SetKindFilterAsync(kind);
        var activeButton = kind switch
        {
            ClipKind.Text => TextFilterButton,
            ClipKind.Image => ImageFilterButton,
            _ => AllFilterButton
        };
        SetActiveFilter(activeButton);
        FocusSearch();
    }

    private void SetActiveFilter(System.Windows.Controls.Button activeButton)
    {
        foreach (var button in new[] { AllFilterButton, TextFilterButton, ImageFilterButton, DeveloperFilterButton })
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(ForegroundProperty);
            button.ClearValue(FontWeightProperty);
        }
        activeButton.SetResourceReference(BackgroundProperty, "GreenSoftBrush");
        activeButton.SetResourceReference(ForegroundProperty, "GreenBrush");
        activeButton.FontWeight = FontWeights.SemiBold;
    }

    private async void PasteClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ClipboardClip clip })
        {
            await _viewModel.PasteClipAsync(clip);
            if (_viewModel.LastPasteSent)
                Hide();
            else
                DockedActionHint.Text = _viewModel.StatusMessage;
        }
    }

    private async void ClipList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(ClipList, source) is not ListBoxItem item)
            return;
        ClipList.SelectedItem = item.DataContext;
        await PasteQuickActionAsync(alternate: false);
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
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _settings.EnableFavoriteQuickAction)
        {
            e.Handled = true;
            await _viewModel.ToggleFavoriteAsync(_viewModel.SelectedClip);
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
            DockedActionHint.Text = _viewModel.StatusMessage;
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.ToolTip = Topmost ? "Desfijar panel" : "Fijar encima";
        if (Topmost)
        {
            PinButton.SetResourceReference(BackgroundProperty, "GreenSoftBrush");
            PinButton.SetResourceReference(ForegroundProperty, "GreenBrush");
        }
        else
        {
            PinButton.Background = System.Windows.Media.Brushes.Transparent;
            PinButton.SetResourceReference(ForegroundProperty, "SoftTextBrush");
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
}
