using System.Windows;
using System.Windows.Media;

namespace Vector2LevelEditor.Services;

public static class EditorTheme
{
    private static readonly Dictionary<string, SolidColorBrush> SharedBrushes = new();

    public static Brush SharedBrush(string key)
    {
        if (SharedBrushes.TryGetValue(key, out var shared)) return shared;
        var source = Application.Current?.TryFindResource(key) as SolidColorBrush;
        shared = new SolidColorBrush(source?.Color ?? Colors.Transparent);
        SharedBrushes[key] = shared;
        return shared;
    }

    public static void Apply(bool dark)
    {
        Set("ChromeBrush", dark ? "#24282F" : "#F4F5F8");
        Set("TabStripBrush", dark ? "#292D34" : "#E7E2DA");
        Set("PanelBrush", dark ? "#1F2227" : "#FAFAFC");
        Set("PanelHeaderBrush", dark ? "#2B3038" : "#E7EAF0");
        Set("CanvasBrush", dark ? "#181B20" : "#FFFFFF");
        Set("GridLineBrush", dark ? "#343A44" : "#D9DEE5");
        Set("WindowTextBrush", dark ? "#F1F3F5" : "#202124");
        Set("MutedTextBrush", dark ? "#AAB2BF" : "#6B7280");
        Set("MenuBrush", dark ? "#252A31" : "#EEF1F6");
        Set("WorkspaceBrush", dark ? "#181B20" : "#202326");
        Set("ToolRailBrush", dark ? "#252A31" : "#F4F5F8");
        Set("ToolButtonBrush", dark ? "#252A31" : "#F5F6FA");
        Set("ToolButtonSelectedBrush", dark ? "#254A67" : "#D7E8FF");
        Set("ControlBrush", dark ? "#30353D" : "#FFFFFF");
        Set("ControlBorderBrush", dark ? "#505966" : "#C7CBD1");
        Set("SettingsOverlayBrush", dark ? "#AA000000" : "#66000000");
        Set("SettingsCardBrush", dark ? "#252A31" : "#F7F8FA");
        Set("ProjectManagerWorkspaceBrush", dark ? "#181C22" : "#FFFFFF");
        Set("ProjectManagerSidebarBrush", dark ? "#20252C" : "#EAF4FF");
        Set("ProjectManagerTopBarBrush", dark ? "#23282F" : "#FFFFFF");
        Set("ProjectManagerCardBrush", dark ? "#252A31" : "#FFFFFF");
        Set("ProjectManagerFieldBrush", dark ? "#1E2228" : "#FFFFFF");
        Set("ProjectManagerEdgeBrush", dark ? "#414852" : "#D5DBE4");
        Set("ProjectManagerMutedBrush", dark ? "#A6AFBC" : "#667085");
        Set("ProjectManagerHoverBrush", dark ? "#39414B" : "#E6ECF4");
        Set("ProjectManagerArtworkBrush", dark ? "#20252C" : "#E9EEF5");

        foreach (Window window in Application.Current.Windows)
        {
            InvalidateTree(window);
        }
    }

    private static void Set(string key, string color)
    {
        if (Application.Current is null) return;
        var value = (Color)ColorConverter.ConvertFromString(color);
        if (SharedBrushes.TryGetValue(key, out var shared)) shared.Color = value;
        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = value;
        else
            Application.Current.Resources[key] = new SolidColorBrush(value);
    }

    private static void InvalidateTree(DependencyObject node)
    {
        if (node is UIElement element) element.InvalidateVisual();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            InvalidateTree(VisualTreeHelper.GetChild(node, index));
        }
    }
}
