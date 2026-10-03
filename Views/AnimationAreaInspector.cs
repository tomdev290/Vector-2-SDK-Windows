using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Views;

public sealed class AnimationAreaInspector : UserControl
{
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(nameof(SelectedNode),
        typeof(LevelNode), typeof(AnimationAreaInspector), new PropertyMetadata(null, (owner, _) => ((AnimationAreaInspector)owner).Render()));
    public LevelNode? SelectedNode { get => (LevelNode?)GetValue(SelectedNodeProperty); set => SetValue(SelectedNodeProperty, value); }
    public static readonly string[] CommonNames = ["RunInhibition", "RunFromInhibition", "RunFast", "RunReverse",
        "RunReverseFromInhibition", "HighJump", "HurdleJump", "HurdleJumpToFly", "WallJump", "WallRunFromFail",
        "WallRunFromFailReverse", "NoWallRun", "CrawlingStart", "CrawlingMiddle", "CrawlingFinish"];
    public AnimationAreaInspector() { Render(); }
    private void Render()
    {
        Visibility = SelectedNode?.Kind == LevelNodeKind.Area ? Visibility.Visible : Visibility.Collapsed;
        if (SelectedNode is null) return;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(new TextBlock { Text = "ANIMATION AREA", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        IReadOnlyList<string> names = CommonNames;
        try { var discovered = new Services.GameTrickPreviewService().LoadAnimationAreaNames(); if (discovered.Count > 0) names = discovered; }
        catch (Exception error) { Diagnostics.DiagnosticsLog.Warn("Animation area catalogue unavailable: " + error.Message); }
        var picker = new ComboBox { IsEditable = true, ItemsSource = names.Append(SelectedNode.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(), ToolTip = "Vector animation AreaName" };
        picker.SetBinding(ComboBox.TextProperty, new Binding(nameof(LevelNode.Name)) { Source = SelectedNode, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        panel.Children.Add(picker);
        Content = panel;
    }
}
