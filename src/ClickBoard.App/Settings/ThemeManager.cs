using System.Windows;
using System.Windows.Media;

namespace ClickBoard.App.Settings;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["CanvasBrush"] = "#F3F3F9",
        ["SidebarBrush"] = "#ECECF4",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SoftSurfaceBrush"] = "#F7F7FC",
        ["TextBrush"] = "#1C1B29",
        ["MutedBrush"] = "#6E6D82",
        ["LineBrush"] = "#E1E0EC",
        ["AccentBrush"] = "#6B4EFF",
        ["AccentSoftBrush"] = "#EDE8FF",
        ["GreenBrush"] = "#1F8A5B",
        ["GreenSoftBrush"] = "#E3F5EC",
        ["InputBrush"] = "#FFFFFF",
        ["HoverBrush"] = "#EFEFF8",
        ["NavHoverBrush"] = "#E5E4F0",
        ["SelectionBrush"] = "#ECE8FF",
        ["SelectionBorderBrush"] = "#B8A8FF",
        ["SelectedHoverBrush"] = "#E4DEFF",
        ["GreenCardBrush"] = "#EAE8F6",
        ["GreenCardTextBrush"] = "#524E72",
        ["ThumbnailBrush"] = "#EFEFF7",
        ["PreviewBrush"] = "#F6F6FB",
        ["PlaceholderBrush"] = "#ECECF5",
        ["PlaceholderIconBrush"] = "#7B7A92",
        ["SoftTextBrush"] = "#56556B",
        ["FaintTextBrush"] = "#9493A8",
        ["DockSurfaceBrush"] = "#F9F9FD",
        ["DockBorderBrush"] = "#DCDCE8",
        ["DockHoverBrush"] = "#EFEFF8",
        ["DockSelectionBrush"] = "#ECE8FF",
        ["DockSelectedHoverBrush"] = "#E3DDFF",
        ["DockInputBorderBrush"] = "#D6D5E4",
        ["QuickSelectedBrush"] = "#ECE8FF",
        ["QuickSelectedTextBrush"] = "#1C1B29",
        ["QuickSelectedMutedBrush"] = "#5A5575",
        ["QuickHoverBrush"] = "#EFEFF8",
        ["QuickKeyBrush"] = "#E6E5F0",
        ["QuickKeyTextBrush"] = "#2E2C40"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["CanvasBrush"] = "#1C1B29",
        ["SidebarBrush"] = "#161521",
        ["SurfaceBrush"] = "#252436",
        ["SoftSurfaceBrush"] = "#2C2B40",
        ["TextBrush"] = "#F4F4F9",
        ["MutedBrush"] = "#9E9DB6",
        ["LineBrush"] = "#34334A",
        ["AccentBrush"] = "#A385FF",
        ["AccentSoftBrush"] = "#2F284E",
        ["GreenBrush"] = "#6CCB94",
        ["GreenSoftBrush"] = "#1F362B",
        ["InputBrush"] = "#2A293D",
        ["HoverBrush"] = "#2F2E44",
        ["NavHoverBrush"] = "#252438",
        ["SelectionBrush"] = "#312B4F",
        ["SelectionBorderBrush"] = "#6D56BA",
        ["SelectedHoverBrush"] = "#38315A",
        ["GreenCardBrush"] = "#232234",
        ["GreenCardTextBrush"] = "#B4B3CA",
        ["ThumbnailBrush"] = "#2F2E44",
        ["PreviewBrush"] = "#1F1E2E",
        ["PlaceholderBrush"] = "#28273B",
        ["PlaceholderIconBrush"] = "#8887A4",
        ["SoftTextBrush"] = "#B8B7CD",
        ["FaintTextBrush"] = "#6E6D88",
        ["DockSurfaceBrush"] = "#1E1D2C",
        ["DockBorderBrush"] = "#36354C",
        ["DockHoverBrush"] = "#2C2B40",
        ["DockSelectionBrush"] = "#312B4F",
        ["DockSelectedHoverBrush"] = "#39325C",
        ["DockInputBorderBrush"] = "#3D3C56",
        ["QuickSelectedBrush"] = "#3A3163",
        ["QuickSelectedTextBrush"] = "#FFFFFF",
        ["QuickSelectedMutedBrush"] = "#D6CEFA",
        ["QuickHoverBrush"] = "#2C2B40",
        ["QuickKeyBrush"] = "#323147",
        ["QuickKeyTextBrush"] = "#E6E5F4"
    };

    public static void Apply(string theme)
    {
        var isDark = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ||
                     theme.Equals("System", StringComparison.OrdinalIgnoreCase) && IsSystemDark();
        var palette = isDark ? DarkPalette : LightPalette;
        foreach (var (key, value) in palette)
            System.Windows.Application.Current.Resources[key] =
                new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
    }

    private static bool IsSystemDark()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int lightMode && lightMode == 0;
        }
        catch
        {
            return true;
        }
    }
}
