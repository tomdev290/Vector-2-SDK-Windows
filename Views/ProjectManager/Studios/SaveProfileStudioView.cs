using System.Globalization;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vector2LevelEditor.Controls;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed record SaveProfile(string Name, string Zone, string Credits, string Premium, string Points,
    string Energy, string Cards, string Runs, string Protocol, string? Model,
    IReadOnlyList<string> ModelLayers, IReadOnlyList<(string Slot, string Name)> Equipment);

public static class PlayerSaveService
{
    private static readonly (string Key, string Path, string Attribute)[] Fields =
    [
        ("Credits", "/User/Items/Stash/Item[@Name='Money']/Group[@Name='Countable']", "Quantity"),
        ("Premium", "/User/Items/Stash/Item[@Name='MoneyPremium']/Group[@Name='Countable']", "Quantity"),
        ("Points", "/User/Items/Stash/Item[@Name='Points']/Group[@Name='Countable']", "Quantity"),
        ("Energy", "/User/UserProperties/Energy", "Level"),
        ("Cards", "/User/UserCounters/Namespace[@Name='ST_Statistics']/Counter[@Name='CardsCount']", "Value"),
        ("Runs", "/User/UserCounters/Namespace[@Name='ST_Statistics']/Counter[@Name='ST_run_counter']", "Value"),
        ("Current custom zone ID", "/User/UserProperties/Zones", "CurrentCustom")
    ];

    public static string? FindSave(string? gameData)
    {
        var livePaths = new[] { Path.Combine(CustomContentService.DefaultStorageRoot, "userdata", "user.xml"),
            Path.Combine(CustomContentService.DefaultStorageRoot, "user.xml") };
        var live = livePaths.FirstOrDefault(File.Exists);
        if (live is not null || string.IsNullOrWhiteSpace(gameData)) return live;
        return new[] { Path.Combine(gameData, "userdata", "user.xml"),
            Path.Combine(gameData, "saved_users", "user_z2_default.xml") }.FirstOrDefault(File.Exists);
    }

    public static SaveProfile Load(string path, string? gameData)
    {
        var doc = XDocument.Load(path);
        var values = Fields.ToDictionary(field => field.Key, field => (string?)doc.XPathSelectElement(field.Path)?.Attribute(field.Attribute) ?? "0");
        var equipped = doc.XPathSelectElements("/User/Items/Equipped/Item").ToList();
        var protocolNames = equipped.Where(item => (string?)item.Attribute("ItemType") == "StarterPack")
            .Select(item => (string?)item.Attribute("Name") ?? "").Where(name => name.Length > 0).ToList();
        var equipment = equipped.Where(item => (string?)item.Attribute("ItemType") != "StarterPack")
            .Select(item => (
                Slot: (string?)item.Elements("Group").FirstOrDefault(group => (string?)group.Attribute("Name") == "ST_MyGadgets")?.Attribute("ST_Slot") ?? (string?)item.Attribute("Name") ?? "Gear",
                Name: (string?)item.Attribute("Name") ?? "Gear")).ToList();
        var layers = equipped.Select(item => (string?)item.Elements("Group").FirstOrDefault(group => (string?)group.Attribute("Name") == "ST_Model")?.Attribute("ST_File"))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => NormalizeSkin(value!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!equipment.Any(item => item.Slot.Equals("Head", StringComparison.OrdinalIgnoreCase))) layers.Add("hair.xml");
        var zone = values["Current custom zone ID"];
        if (zone is "" or "0") zone = (string?)doc.XPathSelectElement("/User/UserProperties/Zones")?.Attribute("Current") ?? "No zone";
        var (protocol, model) = FindInstalledProtocol(gameData, values["Current custom zone ID"], protocolNames);
        return new SaveProfile((string?)doc.Root?.Attribute("Name") ?? "Player", zone, values["Credits"], values["Premium"],
            values["Points"], values["Energy"], values["Cards"], values["Runs"],
            protocol ?? (protocolNames.Count == 0 ? "No protocol equipped" : string.Join(", ", protocolNames)),
            model, layers, equipment);
    }

    public static string NormalizeSkin(string value)
    {
        value = value.Trim();
        return value.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? value : value + ".xml";
    }

    public static string Save(string path, IReadOnlyDictionary<string, string> edits)
    {
        var doc = XDocument.Load(path);
        foreach (var field in Fields)
        {
            if (!edits.TryGetValue(field.Key, out var value)) throw new InvalidDataException($"Missing {field.Key}.");
            if (field.Key != "Current custom zone ID" &&
                (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0))
                throw new InvalidDataException($"{field.Key} must be a whole number from 0 to {int.MaxValue}.");
            if (field.Key == "Current custom zone ID" && value.Length > 128)
                throw new InvalidDataException("The zone ID is too long.");
            var node = doc.XPathSelectElement(field.Path) ?? throw new InvalidDataException($"The save is missing {field.Key}. No changes were written.");
            node.SetAttributeValue(field.Attribute, value.Trim());
        }
        var backup = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.before-editor-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.xml");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            doc.Save(temporary);
            File.Copy(path, backup);
            File.Move(temporary, path, true);
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static (string? Name, string? Model) FindInstalledProtocol(string? gameData, string zone, IReadOnlyList<string> bases)
    {
        if (string.IsNullOrWhiteSpace(gameData) || zone is "" or "0" || bases.Count == 0) return (null, null);
        var contentRoot = Directory.Exists(Path.Combine(gameData, "custom_zones")) ? gameData : Vector2GameIntegrationLocator.ModDataRootFor(gameData);
        var zones = Path.Combine(contentRoot, "custom_zones");
        var protocols = Path.Combine(contentRoot, "custom_protocols");
        if (!Directory.Exists(zones) || !Directory.Exists(protocols)) return (null, null);
        string? chapter = null;
        foreach (var file in Directory.EnumerateFiles(zones, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                chapter = (string?)XDocument.Load(file).Descendants("Zone")
                    .FirstOrDefault(node => (string?)node.Attribute("Id") == zone)?.Attribute("Chapter");
                if (chapter is not null) break;
            }
            catch (System.Xml.XmlException) { }
        }
        if (string.IsNullOrWhiteSpace(chapter)) return (null, null);
        foreach (var file in Directory.EnumerateFiles(protocols, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                var node = XDocument.Load(file).Descendants("Protocol").FirstOrDefault(item =>
                    string.Equals((string?)item.Attribute("Chapter"), chapter, StringComparison.OrdinalIgnoreCase) &&
                    bases.Contains((string?)item.Attribute("BaseProtocol") ?? "BasicProtocol", StringComparer.OrdinalIgnoreCase));
                if (node is not null) return ((string?)node.Attribute("Name"), (string?)node.Attribute("PlayerModel"));
            }
            catch (System.Xml.XmlException) { }
        }
        return (null, null);
    }
}

public sealed class SaveProfileStudioView : UserControl
{
    private readonly string? _savePath;
    private readonly string? _gameData;
    private readonly Action<string> _status;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly StackPanel _body = new() { Margin = new Thickness(24) };
    private readonly System.Windows.Threading.DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Windows.Threading.DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private TrickPreviewControl? _playerPreview;
    private DateTime _lastWrite;
    private bool _editing;

    public SaveProfileStudioView(string projectRoot, string? gameData, Action<string> status)
    {
        _gameData = gameData;
        _status = status;
        _savePath = PlayerSaveService.FindSave(gameData);
        Content = StudioUi.Shell("Player Save", "View and edit the save file used by Vector 2.", "\uE8F7",
            new SolidColorBrush(Color.FromRgb(38, 157, 174)),
            new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Render();
        _refresh.Tick += (_, _) =>
        {
            if (_editing || _savePath is null || !File.Exists(_savePath)) return;
            var stamp = File.GetLastWriteTimeUtc(_savePath);
            if (stamp != _lastWrite) Render();
        };
        Loaded += (_, _) => _refresh.Start();
        Loaded += (_, _) => _animationTimer.Start();
        Unloaded += (_, _) => { _refresh.Stop(); _animationTimer.Stop(); };
        _animationTimer.Tick += (_, _) =>
        {
            if (_playerPreview?.Playback is { Frames.Count: > 0 } playback)
                _playerPreview.Frame = _playerPreview.Frame >= playback.Frames.Count - 1 ? 0 : _playerPreview.Frame + 1;
        };
    }

    private void Render()
    {
        _body.Children.Clear();
        if (_savePath is null || !File.Exists(_savePath))
        {
            _body.Children.Add(new TextBlock { Text = _gameData is null ? "Connect Vector 2 Data from Overview to show the live player save." : "No player save exists in this Vector 2 data folder yet.", FontSize = 16, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
            return;
        }
        SaveProfile profile;
        try { profile = PlayerSaveService.Load(_savePath, _gameData); _lastWrite = File.GetLastWriteTimeUtc(_savePath); }
        catch (Exception ex) { _body.Children.Add(new TextBlock { Text = $"Could not read the player save: {ex.Message}" }); return; }
        var columns = new Grid { MaxWidth = 1250 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var player = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        try
        {
            var previewService = new GameTrickPreviewService();
            var move = previewService.LoadMoves().FirstOrDefault(item => item.Name.Equals("RunForward", StringComparison.OrdinalIgnoreCase));
            if (move is not null)
            {
                _playerPreview = new TrickPreviewControl { Height = 300, Margin = new Thickness(0, 0, 0, 14),
                    ShowDebugEdges = false, ShowModelNodePoints = false, ShowModelOutlines = false, CenterOnVisibleModel = true, ClipToBounds = true, PreviewBackground = new SolidColorBrush(Color.FromRgb(231, 237, 243)),
                    Playback = previewService.LoadPlayback(move, new[] { "0.xml", "1.xml" }.Concat(
                        new[] { profile.Model }.Concat(profile.ModelLayers).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)).Distinct().ToList(), 0) };
                player.Children.Add(_playerPreview);
            }
        }
        catch { _playerPreview = null; }
        player.Children.Add(new TextBlock { Text = profile.Name, FontSize = 22, FontWeight = FontWeights.Bold });
        player.Children.Add(new TextBlock { Text = profile.Zone, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 4, 0, 16) });
        player.Children.Add(StudioUi.Label("Player model"));
        player.Children.Add(new TextBlock { Text = profile.Model ?? "Default Vector body", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (profile.ModelLayers.Count > 0) player.Children.Add(new TextBlock { Text = "Armor: " + string.Join(" + ", profile.ModelLayers), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        player.Children.Add(StudioUi.Button("Refresh Live Save", (_, _) => Render(), true));
        columns.Children.Add(player);
        var detail = new StackPanel();
        detail.Children.Add(StudioUi.Heading("Active Protocol", "\uE8D7", Color.FromRgb(38, 157, 174)));
        detail.Children.Add(new TextBlock { Text = profile.Protocol, FontSize = 16, FontWeight = FontWeights.SemiBold });
        detail.Children.Add(new TextBlock { Text = "Currently Equipped", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 19, 0, 8) });
        if (profile.Equipment.Count == 0) detail.Children.Add(new TextBlock { Text = "No equipment", Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        foreach (var item in profile.Equipment)
            detail.Children.Add(new TextBlock { Text = $"{item.Slot}   {item.Name}", Margin = new Thickness(0, 4, 0, 4) });
        detail.Children.Add(new TextBlock { Text = "Player Data", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 19, 0, 8) });
        detail.Children.Add(new TextBlock { Text = _savePath, TextWrapping = TextWrapping.Wrap, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 8) });
        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var (label, value) in new[] { ("Credits", profile.Credits), ("Premium", profile.Premium), ("Points", profile.Points), ("Runs", profile.Runs), ("Energy", profile.Energy), ("Cards", profile.Cards), ("Zone", profile.Zone) })
        {
            var stat = new StackPanel();
            stat.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            stat.Children.Add(StudioUi.Label(label));
            stats.Children.Add(StudioUi.Card(stat, new Thickness(0, 0, 8, 8)));
        }
        detail.Children.Add(stats);
        var editHeader = new DockPanel { Margin = new Thickness(0, 18, 0, 8) };
        var button = StudioUi.Button(_editing ? "Cancel" : "Edit", (_, _) => { _editing = !_editing; Render(); });
        DockPanel.SetDock(button, Dock.Right);
        editHeader.Children.Add(button);
        editHeader.Children.Add(new TextBlock { Text = "Edit Save Data", FontSize = 16, FontWeight = FontWeights.SemiBold });
        detail.Children.Add(editHeader);
        if (_editing)
        {
            _fields.Clear();
            var inputs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            foreach (var (label, value) in new[] { ("Credits", profile.Credits), ("Premium", profile.Premium), ("Points", profile.Points), ("Energy", profile.Energy), ("Cards", profile.Cards), ("Runs", profile.Runs), ("Current custom zone ID", profile.Zone == "No zone" ? "" : profile.Zone) })
            {
                var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                stack.Children.Add(StudioUi.Label(label));
                var field = StudioUi.Field(value);
                _fields[label] = field;
                stack.Children.Add(field);
                inputs.Children.Add(stack);
            }
            detail.Children.Add(inputs);
            detail.Children.Add(StudioUi.Button("Save Player Data", Save, true));
        }
        Grid.SetColumn(detail, 1);
        columns.Children.Add(detail);
        _body.Children.Add(columns);
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (_savePath is null) return;
        try
        {
            if (File.GetLastWriteTimeUtc(_savePath) != _lastWrite)
                throw new IOException("The game changed this save while you were editing. Cancel and reload before applying your changes.");
            var processes = System.Diagnostics.Process.GetProcessesByName("Vector 2");
            var gameRunning = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (processes.Length > 1) throw new IOException("Close the extra Vector 2 instances before editing the shared player save.");
            if (gameRunning && MessageBox.Show("Applying player data reloads the running game's save and returns it to the main menu. Continue?", "Apply Player Data", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            var backup = PlayerSaveService.Save(_savePath, _fields.ToDictionary(pair => pair.Key, pair => pair.Value.Text));
            _editing = false;
            Render();
            if (gameRunning)
            {
                var reloaded = await Vector2PlayBridge.ReloadPlayerSaveAsync();
                _status(reloaded ? $"Player data saved and reloaded in the running game. Backup: {Path.GetFileName(backup)}." :
                    $"Player data saved to disk, but the running game did not accept reload user. Restart before relying on these values; its current in-memory save may overwrite them. Backup: {Path.GetFileName(backup)}.");
            }
            else _status($"Player data saved. The next game launch will read it. Backup: {Path.GetFileName(backup)}.");
        }
        catch (Exception ex) { _status($"Could not save player data: {ex.Message}"); }
    }
}
