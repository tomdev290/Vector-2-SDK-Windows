using System.Windows;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class HelpWindow : Window
{
    private static readonly IReadOnlyDictionary<EditorTool, string> ToolDescriptions =
        new Dictionary<EditorTool, string>
        {
            [EditorTool.Cursor] = "Select, drag, resize, and rotate objects.",
            [EditorTool.Images] = "Place texture images selected in the Asset Browser.",
            [EditorTool.Backgrounds] = "Place background-tagged texture images.",
            [EditorTool.Trapezoid] = "Place slope collision; hold Y while placing for Type 2.",
            [EditorTool.Collision] = "Place rectangular platform collision.",
            [EditorTool.Trigger] = "Place trigger regions.",
            [EditorTool.Area] = "Place Area regions.",
            [EditorTool.Coin] = "Place bonus pickups.",
            [EditorTool.Camera] = "Place camera and zoom helpers.",
            [EditorTool.Comment] = "Place editor-only comment regions; they are not exported.",
            [EditorTool.ObjectReference] = "Place object references selected in the Asset Browser.",
            [EditorTool.Object] = "Place library objects selected in the Asset Browser.",
            [EditorTool.Dynamic] = "Open Dynamic Studio for movement, size, and rotation keyframes.",
            [EditorTool.PlayerIn] = "Place the player start gate.",
            [EditorTool.PlayerOut] = "Place the level exit gate.",
            [EditorTool.RunFast] = "Place a RunFast area exported as Type=Animation.",
            [EditorTool.Swarm] = "Place a swarm helper.",
            [EditorTool.Waypoint] = "Place a waypoint helper."
        };

    public IReadOnlyList<HelpToolGuide> ToolGuides { get; } = EditorToolInfo.Ordered
        .Select(tool => new HelpToolGuide(new ToolButtonViewModel(tool), ToolDescriptions[tool]))
        .ToList();

    public HelpWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record HelpToolGuide(ToolButtonViewModel Tool, string Description);
