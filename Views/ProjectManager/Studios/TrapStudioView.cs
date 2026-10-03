using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Xml.Linq;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class TrapStudioView : UserControl
{
    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly ListBox _traps = new();
    private readonly StackPanel _editor = new() { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBox _newName = StudioUi.Field();
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly Dictionary<string, ComboBox> _choices = new();
    private readonly Canvas _preview = new() { Width = 470, Height = 320, ClipToBounds = true };
    private readonly ComboBox _visualState = new() { ItemsSource = new[] { "Regular", "Charge up", "Disabled", "Player collision" }, SelectedIndex = 0, Width = 180 };
    private readonly Dictionary<string, UIElement[]> _controls = new();
    private readonly Dictionary<string, string> _texturePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private XDocument? _document;
    private string? _manifest;

    public TrapStudioView(string root, Action<string> status)
    {
        _root = root; _folder = Path.Combine(root, "custom_traps"); _status = status;
        _visualState.SelectionChanged += (_, _) => DrawPreview();
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(255) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(12) };
        var create = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        create.Children.Add(StudioUi.Label("New trap"));
        create.Children.Add(_newName);
        create.Children.Add(StudioUi.Button("Create Trap", (_, _) => Create(), true));
        DockPanel.SetDock(create, Dock.Top); left.Children.Add(create);
        _traps.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_traps);
        workspace.Children.Add(left);
        var divider = new Border { Background = StudioUi.Resource("ProjectManagerEdgeBrush") };
        Grid.SetColumn(divider, 1); workspace.Children.Add(divider);
        var scroll = new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 18, 24, 35) };
        Grid.SetColumn(scroll, 2); workspace.Children.Add(scroll);
        Content = StudioUi.Shell("Trap Designer", "Build traps with their own visuals, hit areas, and runtime triggers.",
            "\uE7BA", new SolidColorBrush(Color.FromRgb(203, 91, 63)), workspace,
            StudioUi.Button("Open Folder", (_, _) => { Directory.CreateDirectory(_folder); Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true }); }),
            StudioUi.Button("New Trap", (_, _) => Create(), true));
        Reload();
    }

    private void Reload(string? selected = null)
    {
        Directory.CreateDirectory(_folder);
        var files = Directory.EnumerateFiles(_folder, "manifest.xml", SearchOption.AllDirectories)
            .OrderBy(Path.GetDirectoryName).ToList();
        _traps.ItemsSource = files;
        _traps.ItemTemplate = FileTemplate();
        _traps.SelectedItem = selected is not null && files.Contains(selected) ? selected : files.FirstOrDefault();
        if (files.Count == 0)
        {
            _editor.Children.Clear();
            _editor.Children.Add(new TextBlock { Text = "Create a trap to set its behavior and hit areas.",
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(30, 120, 0, 0) });
        }
    }

    private static DataTemplate FileTemplate()
    {
        var template = new DataTemplate();
        var block = new FrameworkElementFactory(typeof(TextBlock));
        block.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new TrapNameConverter() });
        block.SetValue(TextBlock.PaddingProperty, new Thickness(9));
        template.VisualTree = block;
        return template;
    }

    private void Create()
    {
        try
        {
            var name = _newName.Text.Trim();
            var id = ProjectTemplateService.SafeId(name);
            if (id.Length == 0)
            {
                id = "NewTrap";
                for (var index = 2; Directory.Exists(Path.Combine(_folder, id)); index++) id = "NewTrap" + index;
                name = id;
            }
            if (Directory.Exists(Path.Combine(_folder, id))) throw new IOException("A trap with this ID already exists.");
            var manifest = CustomTrapCompiler.DefaultManifest(id, name);
            var path = CustomTrapCompiler.Save(_root, manifest);
            _newName.Clear(); Reload(path);
            _status("Created and compiled " + name + ".");
        }
        catch (Exception ex) { _status("Could not create trap: " + ex.Message); }
    }

    private void LoadSelected()
    {
        if (_preview.Parent is Panel previewParent) previewParent.Children.Remove(_preview);
        if (_visualState.Parent is Panel stateParent) stateParent.Children.Remove(_visualState);
        _manifest = _traps.SelectedItem as string;
        _document = null; _editor.Children.Clear(); _fields.Clear(); _choices.Clear(); _controls.Clear();
        _texturePaths.Clear(); _bitmaps.Clear();
        var textures = Path.Combine(_root, "custom_textures");
        if (Directory.Exists(textures))
            foreach (var path in Directory.EnumerateFiles(textures, "*", SearchOption.AllDirectories).OrderBy(path => path))
                _texturePaths.TryAdd(Path.GetFileName(path), path);
        if (_manifest is null) return;
        try
        {
            _document = XDocument.Load(_manifest);
            var trap = _document.Root;
            if (trap?.Name.LocalName != "CustomTrap") throw new InvalidDataException("Trap manifest has no CustomTrap root.");
            var name = (string?)trap.Attribute("Name") ?? "Custom trap";
            _editor.Children.Add(new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
            var identityStart = _editor.Children.Count;
            _preview.Background = new SolidColorBrush(Color.FromRgb(30, 33, 39));
            _preview.HorizontalAlignment = HorizontalAlignment.Left;
            _preview.Margin = new Thickness(0, 0, 0, 20);
            _editor.Children.Add(_preview);
            _editor.Children.Add(_visualState);
            Section("Identity and artwork", "\uE790", Color.FromRgb(200, 100, 63));
            AddField(trap, "Name", "Name");
            AddField(trap, "ID", "Stable ID", readOnly: true);
            AddChoice(trap, "Mount", "Mounted on", ["Floor", "Wall", "Ceiling", "Floating"]);
            AddArtwork(trap, "Artwork", "Regular artwork");
            var top = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 230 });
            var previewPanel = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
            var identityPanel = new StackPanel();
            var identityControls = _editor.Children.Cast<UIElement>().Skip(identityStart).ToArray();
            foreach (var control in identityControls) _editor.Children.Remove(control);
            foreach (var control in identityControls)
                (control == _preview || control == _visualState ? previewPanel : identityPanel).Children.Add(control);
            Grid.SetColumn(identityPanel, 1); top.Children.Add(previewPanel); top.Children.Add(identityPanel);
            top.SizeChanged += (_, _) =>
            {
                var compact = top.ActualWidth < 760;
                top.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
                top.ColumnDefinitions[1].MinWidth = compact ? 0 : 230;
                top.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(identityPanel, compact ? 0 : 1); Grid.SetRow(identityPanel, compact ? 1 : 0);
                identityPanel.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(0);
                previewPanel.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 22, 0);
            };
            _editor.Children.Add(top);
            Section("Behavior", "\uE7C8", Color.FromRgb(204, 108, 53));
            AddChoice(trap, "Impact", "On contact", ["Instant defeat", "Damage armour", "Knockback only"]);
            AddChoice(trap, "ArmorSlot", "Armor slot", ["Helmet", "Torso", "Hands", "Legs", "Belt"]);
            AddNumber(trap, "DamageAmount", "Charges to remove", 1, 12);
            AddChoice(trap, "EnabledByArea", "Only active while player is nearby", ["true", "false"]);
            Section("Artwork and timing", "\uEB9F", Color.FromRgb(45, 135, 185));
            AddNumber(trap, "Width", "Artwork width", 24, 1200, 10);
            AddNumber(trap, "Height", "Artwork height", 24, 1200, 10);
            foreach (var (key, label) in new[] { ("ChargeArtwork", "Charge artwork"), ("DisabledArtwork", "Disabled artwork"), ("HitArtwork", "Collision artwork") })
                AddArtwork(trap, key, label);
            AddNumber(trap, "ChargeFrames", "Charge frames", 0, 600);
            AddNumber(trap, "HitFrames", "Collision frames", 0, 600);
            Section("Sound states", "\uE767", Color.FromRgb(46, 149, 109));
            foreach (var (key, label) in new[] { ("IdleSound", "Regular"), ("ChargeSound", "Charge up"),
                ("HitSound", "Player collision"), ("DisabledSound", "Disabled") })
                AddAudio(trap, key, label);
            AddField(trap, "SoundVolume", "Volume (0 to 1)");
            Section("Gameplay geometry", "\uE81E", Color.FromRgb(184, 77, 68));
            foreach (var prefix in new[] { "Danger", "Activation" })
            {
                _editor.Children.Add(StudioUi.Label(prefix + " area"));
                foreach (var suffix in new[] { "X", "Y", "Width", "Height" })
                    AddNumber(trap, prefix + suffix, suffix, suffix is "Width" or "Height" ? 1 : -5000, 5000);
            }
            var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            commands.Children.Add(StudioUi.Button("Delete", (_, _) => Delete()));
            commands.Children.Add(StudioUi.Button("Reload", (_, _) => LoadSelected()));
            commands.Children.Add(StudioUi.Button("Save & Compile", (_, _) => Save(), true));
            _editor.Children.Add(commands);
            UpdateBehaviorControls();
            DrawPreview();
        }
        catch (Exception ex) { _status("Could not read trap: " + ex.Message); }
    }

    private void Section(string title, string glyph, Color color) =>
        _editor.Children.Add(StudioUi.Heading(title, glyph, color));

    private void AddField(XElement trap, string key, string label, bool readOnly = false)
    {
        _editor.Children.Add(StudioUi.Label(label));
        var field = StudioUi.Field((string?)trap.Attribute(key) ?? "");
        field.IsReadOnly = readOnly; _fields[key] = field; _editor.Children.Add(field);
        field.TextChanged += (_, _) => DrawPreview();
    }

    private void AddNumber(XElement trap, string key, string label, int minimum, int maximum, int step = 1)
    {
        var caption = StudioUi.Label(label);
        _editor.Children.Add(caption);
        var field = StudioUi.Field((string?)trap.Attribute(key) ?? "0");
        var control = StudioUi.Number(field, minimum, maximum, step);
        _fields[key] = field; _editor.Children.Add(control); _controls[key] = [caption, control];
        field.TextChanged += (_, _) => DrawPreview();
    }

    private void AddChoice(XElement trap, string key, string label, string[] values)
    {
        var caption = StudioUi.Label(label);
        _editor.Children.Add(caption);
        var current = (string?)trap.Attribute(key) ?? values[0];
        var picker = new ComboBox { ItemsSource = values.Append(current).Distinct().ToArray(),
            SelectedItem = current, Margin = new Thickness(0, 4, 0, 12) };
        _choices[key] = picker;
        UIElement control = picker;
        if (key == "EnabledByArea")
        {
            var toggle = new CheckBox { IsChecked = current != "false", Margin = new Thickness(0, 4, 0, 12) };
            toggle.SetResourceReference(StyleProperty, "StudioSwitch");
            toggle.Checked += (_, _) => picker.SelectedItem = "true";
            toggle.Unchecked += (_, _) => picker.SelectedItem = "false";
            control = toggle;
        }
        else if (key is "Mount" or "Impact") control = StudioUi.Segments(picker);
        _editor.Children.Add(control);
        _controls[key] = [caption, control];
        picker.SelectionChanged += (_, _) => { UpdateBehaviorControls(); DrawPreview(); };
    }

    private void UpdateBehaviorControls()
    {
        var armour = _choices.TryGetValue("Impact", out var impact) && (string?)impact.SelectedItem == "Damage armour";
        foreach (var key in new[] { "ArmorSlot", "DamageAmount" })
            if (_controls.TryGetValue(key, out var controls))
                foreach (var control in controls) control.Visibility = armour ? Visibility.Visible : Visibility.Collapsed;
        var enabled = !_choices.TryGetValue("EnabledByArea", out var area) || (string?)area.SelectedItem != "false";
        foreach (var key in new[] { "ActivationX", "ActivationY", "ActivationWidth", "ActivationHeight" })
            if (_controls.TryGetValue(key, out var controls))
                foreach (var control in controls) { control.IsEnabled = enabled; control.Opacity = enabled ? 1 : .45; }
    }

    private void AddArtwork(XElement trap, string key, string label)
    {
        _editor.Children.Add(StudioUi.Label(label));
        var field = StudioUi.Field((string?)trap.Attribute(key) ?? "");
        _fields[key] = field; _editor.Children.Add(new ProjectArtworkField(_root, field));
        field.TextChanged += (_, _) =>
        {
            if (key == "Artwork" && _fields.TryGetValue("Width", out var width) && _fields.TryGetValue("Height", out var height) &&
                int.TryParse(height.Text, out var currentHeight) && _texturePaths.TryGetValue(field.Text, out var path))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    if (bitmap.PixelHeight > 0) width.Text = Math.Clamp((int)Math.Round(currentHeight * (double)bitmap.PixelWidth / bitmap.PixelHeight), 24, 1200).ToString();
                }
                catch (Exception error) { _status("Could not fit artwork: " + error.Message); }
            }
            DrawPreview();
        };
    }

    private void DrawPreview()
    {
        _preview.Children.Clear();
        const double scale = 0.43;
        const double originX = 235;
        const double originY = 270;
        for (var x = 20.0; x < 470; x += 40)
            _preview.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = 320,
                Stroke = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) });
        for (var y = 20.0; y < 300; y += 40)
            _preview.Children.Add(new Line { X1 = 0, X2 = 470, Y1 = y, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) });
        string Value(string key, string fallback = "") => _fields.TryGetValue(key, out var field) ? field.Text :
            (string?)_document?.Root?.Attribute(key) ?? fallback;
        int Number(string key, int fallback) => int.TryParse(Value(key), out var value) ? value : fallback;
        var width = Math.Clamp(Number("Width", 180), 24, 1200) * scale;
        var height = Math.Clamp(Number("Height", 180), 24, 1200) * scale;
        var artworkKey = _visualState.SelectedIndex switch { 1 => "ChargeArtwork", 2 => "DisabledArtwork", 3 => "HitArtwork", _ => "Artwork" };
        var artwork = Value(artworkKey);
        if (string.IsNullOrWhiteSpace(artwork)) artwork = Value("Artwork");
        var imagePath = _texturePaths.GetValueOrDefault(artwork);
        if (imagePath is not null)
        {
            try
            {
                if (!_bitmaps.TryGetValue(imagePath, out var bitmap))
                {
                    var loaded = new BitmapImage();
                    loaded.BeginInit(); loaded.UriSource = new Uri(imagePath); loaded.DecodePixelWidth = 400;
                    loaded.CacheOption = BitmapCacheOption.OnLoad; loaded.EndInit(); loaded.Freeze();
                    _bitmaps[imagePath] = bitmap = loaded;
                }
                var image = new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.Fill };
                Canvas.SetLeft(image, originX - width / 2); Canvas.SetTop(image, originY - height);
                _preview.Children.Add(image);
            }
            catch { }
        }
        else
        {
            var icon = new TextBlock { Text = "\uE7BA", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 50, Foreground = new SolidColorBrush(Color.FromRgb(220, 145, 80)) };
            Canvas.SetLeft(icon, originX - 25); Canvas.SetTop(icon, originY - 95);
            _preview.Children.Add(icon);
        }
        if ((_choices.TryGetValue("EnabledByArea", out var enabled) ? enabled.SelectedItem as string :
            (string?)_document?.Root?.Attribute("EnabledByArea")) != "false")
            Region("Activation", Color.FromRgb(237, 157, 59));
        Region("Danger", Color.FromRgb(227, 69, 70));
        var floor = new Line { X1 = 0, X2 = 470, Y1 = originY, Y2 = originY,
            Stroke = new SolidColorBrush(Color.FromRgb(145, 155, 168)), StrokeThickness = 2 };
        _preview.Children.Add(floor);

        void Region(string prefix, Color color)
        {
            var region = new Rectangle { Width = Math.Max(1, Number(prefix + "Width", 180) * scale),
                Height = Math.Max(1, Number(prefix + "Height", 180) * scale),
                Fill = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(color), StrokeThickness = 2 };
            Canvas.SetLeft(region, originX + Number(prefix + "X", -90) * scale);
            Canvas.SetTop(region, originY + Number(prefix + "Y", -180) * scale);
            _preview.Children.Add(region);
        }
    }

    private void AddAudio(XElement trap, string key, string label)
    {
        var folder = Path.Combine(_root, "custom_audio");
        var sounds = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => new[] { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".m4a" }
                .Contains(Path.GetExtension(path).ToLowerInvariant())).Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        var current = (string?)trap.Attribute(key) ?? "";
        var stem = Path.GetFileNameWithoutExtension(current);
        if (sounds.Contains(stem, StringComparer.OrdinalIgnoreCase)) trap.SetAttributeValue(key, stem);
        AddChoice(trap, key, label, [.. new[] { "" }.Concat(sounds)]);
    }

    private void Save()
    {
        if (_document?.Root is not { } current || _manifest is null) return;
        try
        {
            var trap = new XElement(current);
            foreach (var entry in _fields) trap.SetAttributeValue(entry.Key, entry.Value.Text.Trim());
            foreach (var entry in _choices) trap.SetAttributeValue(entry.Key, entry.Value.SelectedItem as string ?? "");
            foreach (var key in new[] { "Width", "Height", "DamageAmount", "ChargeFrames", "HitFrames", "CycleFrames", "Reach",
                "DangerX", "DangerY", "DangerWidth", "DangerHeight", "ActivationX", "ActivationY", "ActivationWidth", "ActivationHeight" })
                if (!int.TryParse((string?)trap.Attribute(key), out _)) throw new InvalidDataException(key + " must be a number.");
            if (!double.TryParse((string?)trap.Attribute("SoundVolume"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var volume) || !double.IsFinite(volume) || volume < 0 || volume > 1)
                throw new InvalidDataException("Sound volume must be between 0 and 1.");
            var path = CustomTrapCompiler.Save(_root, trap);
            _status("Saved and compiled " + Path.GetFileName(Path.GetDirectoryName(path)) + ".");
            Reload(path);
        }
        catch (Exception ex) { _status("Could not compile trap: " + ex.Message); }
    }

    private void Delete()
    {
        if (_manifest is null) return;
        if (MessageBox.Show("Delete this trap and its runtime library?", "Delete trap", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var package = Path.GetDirectoryName(_manifest)!;
            if (!Path.GetFullPath(package).StartsWith(Path.GetFullPath(_folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Trap package is outside the project.");
            var id = ProjectTemplateService.SafeId((string?)_document?.Root?.Attribute("ID") ?? "");
            var runtime = Path.Combine(_root, "custom_gamedata", "run_data", "libraries", "v2trap_" + id + ".xml");
            Directory.Delete(package, true);
            if (File.Exists(runtime)) File.Delete(runtime);
            Reload();
            _status("Deleted trap " + id + ".");
        }
        catch (Exception ex) { _status("Could not delete trap: " + ex.Message); }
    }

    private sealed class TrapNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try { return (string?)XDocument.Load((string)value).Root?.Attribute("Name") ?? Path.GetFileName(Path.GetDirectoryName((string)value)) ?? ""; }
            catch { return Path.GetFileName(Path.GetDirectoryName(value as string ?? "")) ?? ""; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
