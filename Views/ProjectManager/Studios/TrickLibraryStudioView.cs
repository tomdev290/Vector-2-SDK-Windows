using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class TrickLibraryStudioView : UserControl
{
    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly Action<string>? _navigate;
    private readonly WrapPanel _items = new();
    private readonly TextBox _search = StudioUi.Field();
    private readonly StackPanel _body = new();
    private readonly UIElement _library;

    public TrickLibraryStudioView(string root, Action<string> status, Action<string>? navigate = null)
    {
        _root = root;
        _folder = Path.Combine(root, "custom_tricks");
        _status = status;
        _navigate = navigate;
        var controls = new Grid { Margin = new Thickness(20, 8, 20, 9) };
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.Width = 270;
        _search.HorizontalAlignment = HorizontalAlignment.Left;
        _search.ToolTip = "Find a trick";
        _search.TextChanged += (_, _) => Reload();
        controls.Children.Add(_search);
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        var shopLink = StudioUi.Button("Shop Cards", (_, _) => _navigate?.Invoke("Shop"));
        shopLink.IsEnabled = Vector2LevelEditor.Models.ProjectManager.ProjectSection.ShopEditingAvailable;
        shopLink.ToolTip = "Shop editing is unavailable in this release.";
        links.Children.Add(shopLink);
        links.Children.Add(StudioUi.Button("Upgrade Paths", (_, _) => _navigate?.Invoke("Upgrades")));
        Grid.SetColumn(links, 1);
        controls.Children.Add(links);
        _body.Children.Add(controls);

        var flow = new Grid { Margin = new Thickness(20, 0, 20, 13) };
        for (var i = 0; i < 3; i++) flow.ColumnDefinitions.Add(new ColumnDefinition());
        var steps = new[] { ("1", "Animation", "Trick Creator"), ("2", "Presentation", "Shop"), ("3", "Progression", "Upgrades") };
        for (var i = 0; i < steps.Length; i++)
        {
            var item = steps[i];
            var block = new StackPanel { Orientation = Orientation.Horizontal };
            block.Children.Add(new TextBlock { Text = item.Item1, Background = new SolidColorBrush(Color.FromRgb(49, 186, 98)), Foreground = Brushes.White, Width = 23, Height = 23, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
            var title = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
            title.Children.Add(new TextBlock { Text = item.Item2, FontSize = 11, FontWeight = FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text = item.Item3, FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
            block.Children.Add(title);
            var border = new Border { Background = new SolidColorBrush(Color.FromRgb(239, 251, 244)), Padding = new Thickness(12), Margin = new Thickness(0, 0, 8, 0), Child = block };
            Grid.SetColumn(border, i);
            flow.Children.Add(border);
        }
        _body.Children.Add(flow);
        _items.Margin = new Thickness(20, 0, 20, 20);
        _body.Children.Add(_items);
        _library = StudioUi.Shell("Trick Library", "View each custom trick, its shop card, and its upgrades.",
            "\uE7FC", new SolidColorBrush(Color.FromRgb(51, 183, 94)),
            new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            StudioUi.Button("Import Existing", (_, _) => Import()),
            StudioUi.Button("New Trick", (_, _) => ShowCreate(), true));
        Content = _library;
        Reload();
    }

    private void Reload()
    {
        Directory.CreateDirectory(_folder);
        _items.Children.Clear();
        var manifests = Directory.EnumerateFiles(_folder, "trick.xml", SearchOption.AllDirectories)
            .OrderBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifests)
        {
            try
            {
                var root = XDocument.Load(file).Root;
                if (root is null || root.Name.LocalName != "CustomTrick") continue;
                var id = (string?)root.Attribute("Name") ?? Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
                var name = (string?)root.Attribute("VisualName") ?? id;
                if (!string.IsNullOrWhiteSpace(_search.Text) &&
                    !name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) &&
                    !id.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                var card = new StackPanel { Width = 230 };
                var preview = new Grid { Height = 125, Background = StudioUi.Resource("ProjectManagerArtworkBrush") };
                var artwork = (string?)root.Attribute("Image") ?? "";
                var artworkPath = Directory.Exists(Path.Combine(_root, "custom_textures"))
                    ? Directory.EnumerateFiles(Path.Combine(_root, "custom_textures"), "*", SearchOption.AllDirectories)
                        .FirstOrDefault(path => Path.GetFileName(path).Equals(artwork, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (artworkPath is not null)
                {
                    try
                    {
                        var image = new BitmapImage();
                        image.BeginInit(); image.UriSource = new Uri(artworkPath); image.CacheOption = BitmapCacheOption.OnLoad;
                        image.DecodePixelWidth = 460; image.EndInit(); image.Freeze();
                        preview.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
                    }
                    catch { }
                }
                if (preview.Children.Count == 0)
                    preview.Children.Add(new TextBlock { Text = "\uE7FC", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(175, 25, 33, 42)), Margin = new Thickness(0) };
                labels.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 5, 8, 0) });
                labels.Children.Add(new TextBlock { Text = id, Foreground = Brushes.White, FontSize = 10, Margin = new Thickness(8, 0, 8, 5) });
                preview.Children.Add(labels);
                card.Children.Add(preview);
                card.Children.Add(new TextBlock { Text = $"{(string?)root.Attribute("FileName") ?? "Animation not linked"}     Level {(string?)root.Attribute("MaxLevel") ?? "1"}", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 7, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis });
                card.Children.Add(new TextBlock { Text = "Imported animation", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(40, 160, 83)) });
                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var shopButton = StudioUi.Button("Shop", (_, _) => _navigate?.Invoke("Shop"));
                shopButton.IsEnabled = Vector2LevelEditor.Models.ProjectManager.ProjectSection.ShopEditingAvailable;
                shopButton.ToolTip = "Shop editing is unavailable in this release.";
                actions.Children.Add(shopButton);
                actions.Children.Add(StudioUi.Button("Upgrades", (_, _) => _navigate?.Invoke("Upgrades")));
                actions.Children.Add(StudioUi.Button("Open", (_, _) => Process.Start(new ProcessStartInfo(Path.GetDirectoryName(file)!) { UseShellExecute = true })));
                actions.Children.Add(StudioUi.Button("Delete", (_, _) => Delete(file)));
                card.Children.Add(actions);
                _items.Children.Add(StudioUi.Card(card, new Thickness(0, 0, 12, 12)));
            }
            catch (Exception ex) { _status($"Could not read {Path.GetFileName(file)}: {ex.Message}"); }
        }
        if (_items.Children.Count == 0)
            _items.Children.Add(new TextBlock { Text = "No complete tricks yet.", Margin = new Thickness(12, 50, 0, 0), FontSize = 13, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
    }

    private void ShowCreate()
    {
        Content = new CustomTrickCreatorView(_root, _status, () => { Content = _library; Reload(); });
    }

    public void OpenCreator() => ShowCreate();

    private void Import()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a trick package containing trick.xml" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var source = dialog.FolderName;
            if (!File.Exists(Path.Combine(source, "trick.xml"))) throw new InvalidDataException("The selected folder has no trick.xml.");
            var target = Path.Combine(_folder, Path.GetFileName(source));
            if (Directory.Exists(target)) throw new IOException("A trick package with this name already exists.");
            if (Path.GetFullPath(source).StartsWith(Path.GetFullPath(_folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This trick is already in the project.");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            Reload();
            _status("Imported trick package " + Path.GetFileName(target) + ".");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Delete(string manifest)
    {
        if (MessageBox.Show("Delete this trick package, including animation and card?", "Delete trick", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var package = Path.GetDirectoryName(manifest)!;
            if (!Path.GetFullPath(package).StartsWith(Path.GetFullPath(_folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This trick has no separate package folder.");
            Directory.Delete(package, true);
            Reload();
            _status("Deleted trick package " + Path.GetFileName(package) + ".");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    public static string Create(string folder, string name)
    {
        var id = ProjectTemplateService.SafeId(name); if (id.Length == 0) throw new InvalidDataException("Enter a stable trick ID.");
        var source = new OpenFileDialog { Title = "Choose the exported trick animation", Filter = "Vector animation (*.bytes)|*.bytes" };
        if (source.ShowDialog() != true) throw new OperationCanceledException("Trick creation was cancelled.");
        var frames = GameTrickPreviewService.ValidateImportedFrames(source.FileName);
        return CustomTrickPackageService.SaveNew(folder, new CustomTrickDraft(name, name, "Custom trick: " + name,
            source.FileName, "DetectorH", 0, 0, Math.Max(0, frames - 2), "RunForward", 1, "", 1, 1000));
    }
}
