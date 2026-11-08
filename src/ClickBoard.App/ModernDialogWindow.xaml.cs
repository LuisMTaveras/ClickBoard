using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WColor = System.Windows.Media.Color;
using WTextBlock = System.Windows.Controls.TextBlock;

namespace ClickBoard.App;

public enum ModernDialogType
{
    Question,
    Warning,
    Danger,
    Info,
    Success
}

public partial class ModernDialogWindow : Window
{
    public bool Result { get; private set; }

    public ModernDialogWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Result = false;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                Result = true;
                Close();
            }
        };
    }

    public static bool ShowConfirm(
        Window? owner,
        string message,
        string title = "Confirmación",
        string confirmText = "Aceptar",
        string cancelText = "Cancelar",
        ModernDialogType type = ModernDialogType.Question)
    {
        var dialog = new ModernDialogWindow();
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.PrimaryBtn.Content = confirmText;
        dialog.SecondaryBtn.Content = cancelText;

        switch (type)
        {
            case ModernDialogType.Danger:
                dialog.IconGlyph.Text = "\uE74D"; // Delete / Trash icon
                dialog.IconBadge.Background = new SolidColorBrush(WColor.FromArgb(40, 235, 87, 87));
                dialog.IconGlyph.Foreground = new SolidColorBrush(WColor.FromRgb(235, 87, 87));
                break;
            case ModernDialogType.Warning:
                dialog.IconGlyph.Text = "\uE7BA"; // Warning triangle
                dialog.IconBadge.Background = new SolidColorBrush(WColor.FromArgb(40, 242, 201, 76));
                dialog.IconGlyph.Foreground = new SolidColorBrush(WColor.FromRgb(242, 201, 76));
                break;
            case ModernDialogType.Info:
                dialog.IconGlyph.Text = "\uE946"; // Info
                dialog.IconBadge.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                dialog.IconGlyph.SetResourceReference(WTextBlock.ForegroundProperty, "AccentBrush");
                break;
            case ModernDialogType.Success:
                dialog.IconGlyph.Text = "\uE73E"; // Checkmark
                dialog.IconBadge.Background = new SolidColorBrush(WColor.FromArgb(40, 39, 174, 96));
                dialog.IconGlyph.Foreground = new SolidColorBrush(WColor.FromRgb(39, 174, 96));
                break;
            case ModernDialogType.Question:
            default:
                dialog.IconGlyph.Text = "\uE897"; // Question mark
                dialog.IconBadge.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                dialog.IconGlyph.SetResourceReference(WTextBlock.ForegroundProperty, "AccentBrush");
                break;
        }

        dialog.PrimaryBtn.Focus();
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static void ShowInfo(
        Window? owner,
        string message,
        string title = "Información",
        string closeText = "Entendido")
    {
        var dialog = new ModernDialogWindow();
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.PrimaryBtn.Content = closeText;
        dialog.SecondaryBtn.Visibility = Visibility.Collapsed;
        dialog.IconGlyph.Text = "\uE946";
        dialog.IconBadge.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
        dialog.IconGlyph.SetResourceReference(WTextBlock.ForegroundProperty, "AccentBrush");

        dialog.PrimaryBtn.Focus();
        dialog.ShowDialog();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
