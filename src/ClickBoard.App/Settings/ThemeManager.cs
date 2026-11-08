using System.Windows;
using System.Windows.Media;

namespace ClickBoard.App.Settings;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["CanvasBrush"] = "#F5F5F2",
        ["SidebarBrush"] = "#F8F8F5",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SoftSurfaceBrush"] = "#F9F9F7",
        ["TextBrush"] = "#24282E",
        ["MutedBrush"] = "#7E858E",
        ["LineBrush"] = "#E8E9E6",
        ["AccentBrush"] = "#3977F6",
        ["AccentSoftBrush"] = "#E8F0FF",
        ["GreenBrush"] = "#28784D",
        ["GreenSoftBrush"] = "#E4F0E7",
        ["InputBrush"] = "#FFFFFF",
        ["HoverBrush"] = "#F1F4F9",
        ["NavHoverBrush"] = "#ECEFF3",
        ["SelectionBrush"] = "#E8F0FF",
        ["SelectionBorderBrush"] = "#BFD2FF",
        ["SelectedHoverBrush"] = "#E4EEFF",
        ["GreenCardBrush"] = "#EDF3EF",
        ["GreenCardTextBrush"] = "#66766A",
        ["ThumbnailBrush"] = "#F2F4F7",
        ["PreviewBrush"] = "#F4F4F1",
        ["PlaceholderBrush"] = "#F0F3F1",
        ["PlaceholderIconBrush"] = "#8B948D",
        ["SoftTextBrush"] = "#647067",
        ["FaintTextBrush"] = "#A2AAA3",
        ["DockSurfaceBrush"] = "#FAFAF7",
        ["DockBorderBrush"] = "#D9DED9",
        ["DockHoverBrush"] = "#F1F5F0",
        ["DockSelectionBrush"] = "#E7F0E7",
        ["DockSelectedHoverBrush"] = "#E4F0E7",
        ["DockInputBorderBrush"] = "#DDE1E7",
        ["QuickSelectedBrush"] = "#DCE8FF",
        ["QuickSelectedTextBrush"] = "#24282E",
        ["QuickSelectedMutedBrush"] = "#58677C",
        ["QuickHoverBrush"] = "#F1F4F9",
        ["QuickKeyBrush"] = "#E8EAEE",
        ["QuickKeyTextBrush"] = "#343A43"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["CanvasBrush"] = "#17191D",
        ["SidebarBrush"] = "#1B1E23",
        ["SurfaceBrush"] = "#1F2228",
        ["SoftSurfaceBrush"] = "#252932",
        ["TextBrush"] = "#E8EAF0",
        ["MutedBrush"] = "#9AA2B1",
        ["LineBrush"] = "#343943",
        ["AccentBrush"] = "#79A7FF",
        ["AccentSoftBrush"] = "#27354D",
        ["GreenBrush"] = "#83C999",
        ["GreenSoftBrush"] = "#253A2E",
        ["InputBrush"] = "#22262D",
        ["HoverBrush"] = "#2B3038",
        ["NavHoverBrush"] = "#2A2F36",
        ["SelectionBrush"] = "#26354D",
        ["SelectionBorderBrush"] = "#3C557F",
        ["SelectedHoverBrush"] = "#2A3B56",
        ["GreenCardBrush"] = "#202F25",
        ["GreenCardTextBrush"] = "#A2C9AC",
        ["ThumbnailBrush"] = "#2A2E35",
        ["PreviewBrush"] = "#22262D",
        ["PlaceholderBrush"] = "#252A30",
        ["PlaceholderIconBrush"] = "#7C8795",
        ["SoftTextBrush"] = "#A2B0A6",
        ["FaintTextBrush"] = "#68717F",
        ["DockSurfaceBrush"] = "#1F2228",
        ["DockBorderBrush"] = "#373D47",
        ["DockHoverBrush"] = "#2B3038",
        ["DockSelectionBrush"] = "#25392D",
        ["DockSelectedHoverBrush"] = "#2A4233",
        ["DockInputBorderBrush"] = "#414752",
        ["QuickSelectedBrush"] = "#254D85",
        ["QuickSelectedTextBrush"] = "#F7F9FC",
        ["QuickSelectedMutedBrush"] = "#D4E0F2",
        ["QuickHoverBrush"] = "#303640",
        ["QuickKeyBrush"] = "#363C46",
        ["QuickKeyTextBrush"] = "#E5E7EB"
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
