using System.Xml.Linq;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class GeneratorStudioView : UserControl
{
    private readonly string _root;
    private readonly Action<string> _status;
    private readonly WrapPanel _pools = new() { Margin = new Thickness(0, 12, 0, 0) };

    public GeneratorStudioView(string root, Action<string> status)
    {
        _root = root; _status = status;
        var content = new StackPanel { Margin = new Thickness(24, 12, 24, 20) };
        var flow = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        for (var i = 0; i < 4; i++) flow.ColumnDefinitions.Add(new ColumnDefinition());
        var steps = new[] { ("\uE768", "Start Run", Color.FromRgb(48, 183, 98)), ("\uE774", "Choose Zone", Color.FromRgb(0, 176, 193)), ("\uE8B7", "Pick Room", Color.FromRgb(40, 130, 226)), ("\uE7F1", "Build Floor", Color.FromRgb(229, 112, 39)) };
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            var background = Color.FromArgb(20, step.Item3.R, step.Item3.G, step.Item3.B);
            var box = new Border { Height = 64, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(background), Margin = new Thickness(0, 0, 12, 0) };
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = step.Item1, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = new SolidColorBrush(step.Item3), FontSize = 16, TextAlignment = TextAlignment.Center });
            labels.Children.Add(new TextBlock { Text = step.Item2, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });
            box.Child = labels;
            Grid.SetColumn(box, i);
            flow.Children.Add(box);
        }
        content.Children.Add(flow);
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Zone Room Pools", FontWeight = FontWeights.Bold, FontSize = 16 });
        title.Children.Add(new TextBlock { Text = "Each zone picks rooms from the folder shown here.", FontSize = 11, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        heading.Children.Add(title);
        var refresh = StudioUi.Button("Refresh", (_, _) => Reload());
        Grid.SetColumn(refresh, 1);
        heading.Children.Add(refresh);
        content.Children.Add(heading);
        content.Children.Add(_pools);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = StudioUi.Shell("Room Generator", "Check which rooms the game can pick for each zone.", "\uE950", new SolidColorBrush(Color.FromRgb(47, 158, 91)), scroll);
        Reload();
    }

    private void Reload()
    {
        _pools.Children.Clear();
        var folder = Path.Combine(_root, "custom_zones");
        foreach (var file in Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories) : [])
        {
            try
            {
                foreach (var zone in XDocument.Load(file).Descendants("Zone"))
                {
                var id = (string?)zone.Attribute("Id") ?? Path.GetFileNameWithoutExtension(file);
                var name = (string?)zone.Attribute("Name") ?? id;
                var relative = (string?)zone.Attribute("RoomsPath") ?? $"custom_rooms/{id}";
                var roomFolder = Vector2LevelEditor.Services.ProjectManager.ProjectManifestService.SafeCombine(_root, relative);
                var rooms = Directory.Exists(roomFolder) ? Directory.EnumerateFiles(roomFolder, "*.xml", SearchOption.AllDirectories).Where(path => !path.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase)).ToList() : [];
                _pools.Children.Add(PoolCard(name, relative, roomFolder, rooms));
                }
            }
            catch (Exception error) { _status("Could not read zone pool: " + error.Message); }
        }
        if (_pools.Children.Count == 0) _pools.Children.Add(new TextBlock { Text = "No zone pools. Create a zone first.", FontSize = 13, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(8, 15, 0, 0) });
    }

    private Border PoolCard(string name, string relative, string folder, List<string> rooms)
    {
        var panel = new StackPanel { Width = 190 };
        panel.Children.Add(new TextBlock { Text = $"{rooms.Count} room{(rooms.Count == 1 ? "" : "s")}", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), HorizontalAlignment = HorizontalAlignment.Right });
        panel.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = relative, FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 3, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var room in rooms.Take(6)) panel.Children.Add(new TextBlock { Text = "\uE8A5  " + Path.GetFileNameWithoutExtension(room), FontSize = 10, Margin = new Thickness(0, 3, 0, 0) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(StudioUi.Button("Open", (_, _) => { Directory.CreateDirectory(folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }));
        actions.Children.Add(StudioUi.Button("Add Rooms", (_, _) => Import(folder), true));
        panel.Children.Add(actions);
        return StudioUi.Card(panel, new Thickness(0, 0, 12, 12));
    }

    private void Import(string folder)
    {
        var dialog = new OpenFileDialog { Filter = "Vector room XML (*.xml)|*.xml", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imported = Vector2LevelEditor.Services.ProjectManager.ZoneRoomImportService.Import(_root,
                Path.GetRelativePath(_root, folder), dialog.FileNames);
            _status($"Added {imported.Count} room(s) to {Path.GetRelativePath(_root, folder)}."); Reload();
        }
        catch (Exception error) { _status("Room import stopped: " + error.Message); }
    }
}
