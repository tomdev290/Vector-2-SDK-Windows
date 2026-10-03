using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class SwarmDesignerWindow : Window
{
    private readonly MainWindowViewModel _editor;
    public LevelDocument Document { get; }

    public SwarmDesignerWindow(LevelDocument document, MainWindowViewModel editor)
    {
        Document = document;
        _editor = editor;
        InitializeComponent();
        Sections.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, Sections) && Sections.SelectedIndex == 2) RunChecks();
        };
        LoadSwarmNames();
        RefreshLists();
    }

    private LevelNode? SelectedWaypoint => WaypointList.SelectedItem as LevelNode;

    private void LoadSwarmNames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GameData", "run_data", "libraries", "swarms.xml");
        try
        {
            var names = XDocument.Load(path).Descendants("Swarm")
                .Select(node => (string?)node.Attribute("Name") ?? "")
                .Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name).ToList();
            SwarmNames.ItemsSource = names.Count > 0 ? names : ["Test"];
        }
        catch { SwarmNames.ItemsSource = new[] { "Test" }; }
        SwarmNames.SelectedIndex = 0;
    }

    private void RefreshLists(Guid? selected = null)
    {
        ActivatorCount.Text = $"Activator  {SwarmDesignerService.References(Document, "SwarmActivator").Count}";
        HoleCount.Text = $"Swarm hole  {SwarmDesignerService.References(Document, "SwarmHole").Count}";
        DeactivatorCount.Text = $"Deactivator  {SwarmDesignerService.References(Document, "SwarmDeactivator").Count}";
        WaypointCount.Text = $"Waypoints  {SwarmDesignerService.Waypoints(Document).Count}";
        var previous = selected ?? SelectedWaypoint?.Id;
        var waypoints = SwarmDesignerService.Waypoints(Document);
        WaypointList.ItemsSource = waypoints;
        WaypointList.SelectedItem = waypoints.FirstOrDefault(node => node.Id == previous)
            ?? waypoints.FirstOrDefault(node => node.Name == "Start") ?? waypoints.FirstOrDefault();
        RefreshWaypoint();
        RunChecks();
    }

    private void RefreshWaypoint()
    {
        var node = SelectedWaypoint;
        WaypointEditor.IsEnabled = node is not null;
        WaypointTitle.Text = node?.Name ?? "Select a waypoint";
        WaypointName.Text = node?.Name ?? "";
        WaypointType.SelectedIndex = node is not null &&
            node.SourceAttributes.GetValueOrDefault("Type") == "Start" ? 1 : 0;
        SpawnX.Text = node?.SourceAttributes.GetValueOrDefault("SpawnX", "0") ?? "";
        SpawnY.Text = node?.SourceAttributes.GetValueOrDefault("SpawnY", "0") ?? "";
        SpawnDelay.Text = node?.SourceAttributes.GetValueOrDefault("SpawnDelay", "0") ?? "";
        WaypointKey.Text = node?.SourceAttributes.GetValueOrDefault("WaypointKey", "") ?? "";
        Speed.Text = node is null ? "" : SwarmDesignerService.Motion(node, "Speed");
        StartAcceleration.Text = node is null ? "" : SwarmDesignerService.Motion(node, "StartAccFrames");
        StopAcceleration.Text = node is null ? "" : SwarmDesignerService.Motion(node, "StopAccFrames");
        ConnectionsList.ItemsSource = node is null ? [] : SwarmDesignerService.Connections(node);
        Destination.ItemsSource = new[] { "next_room" }
            .Concat(SwarmDesignerService.Waypoints(Document).Where(item => item.Id != node?.Id)
                .Select(item => item.Name)).ToList();
        Destination.SelectedIndex = Destination.Items.Count > 0 ? 0 : -1;
    }

    private void SetStatus(string text)
    {
        Status.Text = text;
        _editor.StatusText = text;
    }

    private void CreateBasic_Click(object sender, RoutedEventArgs e)
    {
        var start = SwarmDesignerService.CreateBasic(Document, SwarmNames.SelectedItem as string ?? "Test");
        RefreshLists(start.Id);
        Sections.SelectedIndex = 1;
        SetStatus($"Created a basic swarm path from {start.Name}.");
    }

    private void AddActivator_Click(object sender, RoutedEventArgs e) =>
        AddReference("SwarmActivator");
    private void AddHole_Click(object sender, RoutedEventArgs e) =>
        AddReference("SwarmHole");
    private void AddDeactivator_Click(object sender, RoutedEventArgs e) =>
        AddReference("SwarmDeactivator");

    private void AddReference(string name)
    {
        var node = SwarmDesignerService.AddReference(Document, name);
        _editor.SelectNodeCommand.Execute(node);
        RefreshLists();
        SetStatus($"Added {name} to {Document.Name}.");
    }

    private void AddWaypoint_Click(object sender, RoutedEventArgs e)
    {
        var count = SwarmDesignerService.Waypoints(Document).Count;
        var node = SwarmDesignerService.AddWaypoint(Document, "Waypoint", 500 + count * 180, -300);
        RefreshLists(node.Id);
        SetStatus($"Added {node.Name}.");
    }

    private void WaypointList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshWaypoint();

    private void ApplyWaypoint_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWaypoint is not { } node) return;
        try
        {
            RequireNumber(SpawnX.Text, "Spawn X");
            RequireNumber(SpawnY.Text, "Spawn Y");
            RequireNumber(SpawnDelay.Text, "Spawn delay");
            RequireNumber(Speed.Text, "Speed");
            RequireNumber(StartAcceleration.Text, "Start acceleration");
            RequireNumber(StopAcceleration.Text, "Stop acceleration");
            SwarmDesignerService.RenameWaypoint(Document, node, WaypointName.Text);
            node.SourceAttributes["SpawnX"] = SpawnX.Text.Trim();
            node.SourceAttributes["SpawnY"] = SpawnY.Text.Trim();
            node.SourceAttributes["SpawnDelay"] = SpawnDelay.Text.Trim();
            node.SourceAttributes["WaypointKey"] = WaypointKey.Text.Trim();
            if (WaypointType.SelectedIndex == 1) node.SourceAttributes["Type"] = "Start";
            else node.SourceAttributes.Remove("Type");
            SwarmDesignerService.SetMotion(node, "Speed", Speed.Text);
            SwarmDesignerService.SetMotion(node, "StartAccFrames", StartAcceleration.Text);
            SwarmDesignerService.SetMotion(node, "StopAccFrames", StopAcceleration.Text);
            RefreshLists(node.Id);
            SetStatus($"Updated {node.Name}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void AddPath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWaypoint is not { } node || Destination.SelectedItem is not string destination) return;
        try
        {
            RequireNumber(EdgeDelay.Text, "Delay frames");
            RequireNumber(EdgeSpeed.Text, "Speed change");
            var connections = SwarmDesignerService.Connections(node).ToList();
            if (connections.Any(connection => connection.Name == destination))
                throw new InvalidDataException($"{node.Name} already links to {destination}.");
            connections.Add(new SwarmConnection(destination, EdgeDelay.Text.Trim(), EdgeSpeed.Text.Trim()));
            SwarmDesignerService.SetConnections(node, connections);
            EdgeDelay.Clear();
            EdgeSpeed.Clear();
            RefreshLists(node.Id);
            SetStatus($"Linked {node.Name} to {destination}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void RemovePath_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWaypoint is not { } node || ConnectionsList.SelectedItem is not SwarmConnection connection) return;
        SwarmDesignerService.SetConnections(node, SwarmDesignerService.Connections(node)
            .Where(item => item != connection));
        RefreshLists(node.Id);
        SetStatus($"Removed path from {node.Name} to {connection.Name}.");
    }

    private void ShowOnCanvas_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedWaypoint is not { } node) return;
        _editor.SelectNodeCommand.Execute(node);
        _editor.SelectDocumentCommand.Execute(Document);
        Owner?.Activate();
        SetStatus($"Selected {node.Name} on the canvas.");
    }

    private void RunChecks_Click(object sender, RoutedEventArgs e) => RunChecks();
    private void RunChecks()
    {
        var issues = SwarmDesignerService.Validate(Document);
        CheckSummary.Text = issues.Count == 0 ? "Swarm setup looks valid" :
            $"Found {issues.Count} issue{(issues.Count == 1 ? "" : "s")}";
        Issues.ItemsSource = issues.Count == 0
            ? ["The room has an activator, hole, Start waypoint, and valid path links."]
            : issues;
    }

    private static void RequireNumber(string raw, string label)
    {
        if (raw.Trim().Length > 0 && !double.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException($"{label} must be a number.");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
