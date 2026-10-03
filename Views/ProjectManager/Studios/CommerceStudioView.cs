using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class CommerceStudioView : UserControl
{
    private readonly string _root;
    private readonly bool _shopMode;
    private readonly Action<string> _status;
    private readonly ListBox _catalogue = new();
    private readonly StackPanel _details = new();
    private readonly Grid _shell;
    private readonly TextBox _search = StudioUi.Field();
    private readonly Dictionary<string, TextBox> _inputs = [];
    private CheckBox? _enabled;
    private ComboBox? _rarity;
    private ComboBox? _cardType;
    private ComboBox? _artwork;
    private XDocument? _document;
    private string? _selectedPath;
    private readonly List<(XElement Element, string Attribute, TextBox Input)> _levelInputs = [];

    public CommerceStudioView(string root, bool shopMode, Action<string> status)
    {
        _root = root;
        _shopMode = shopMode;
        _status = status;
        _catalogue.SetResourceReference(BackgroundProperty, "ProjectManagerCardBrush");
        _catalogue.SetResourceReference(ForegroundProperty, "WindowTextBrush");
        _catalogue.BorderThickness = new Thickness(0);
        // The stock disabled ListBox template paints a system-white surface.
        var catalogueBorder = new FrameworkElementFactory(typeof(Border));
        catalogueBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var catalogueScroll = new FrameworkElementFactory(typeof(ScrollViewer));
        catalogueScroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        catalogueScroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        catalogueScroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        catalogueScroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        catalogueBorder.AppendChild(catalogueScroll);
        _catalogue.Template = new ControlTemplate(typeof(ListBox)) { VisualTree = catalogueBorder };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(MarginProperty, new Thickness(4, 3, 4, 0)));
        itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(9, 8, 9, 8)));
        var itemBorder = new FrameworkElementFactory(typeof(Border));
        itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        itemBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        itemBorder.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        itemBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        itemStyle.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = itemBorder }));
        var selection = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selection.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ProjectManagerHoverBrush")));
        itemStyle.Triggers.Add(selection);
        _catalogue.ItemContainerStyle = itemStyle;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new DockPanel { Margin = new Thickness(0, 0, 0, 0), Background = StudioUi.Resource("ProjectManagerCardBrush") };
        var find = new StackPanel { Margin = new Thickness(10, 12, 10, 8) };
        _search.ToolTip = "Find a card";
        _search.TextChanged += (_, _) => Reload(_selectedPath);
        find.Children.Add(StudioUi.Search(_search, "Find a card"));
        DockPanel.SetDock(find, Dock.Top);
        left.Children.Add(find);
        _catalogue.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_catalogue);
        grid.Children.Add(new Border { BorderBrush = StudioUi.Resource("ProjectManagerEdgeBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Child = left });

        var scroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);

        Content = _shell = StudioUi.Shell(shopMode ? "Shop" : "Upgrades",
            shopMode ? "Choose how each card appears and what it costs." : "Set the cost and effect of each upgrade level.",
            shopMode ? "\uE7BF" : "\uE74A",
            new SolidColorBrush(shopMode ? Color.FromRgb(38, 127, 229) : Color.FromRgb(135, 99, 198)),
            grid,
            StudioUi.Button("Delete", (_, _) => Delete()),
            StudioUi.Button("Reload", (_, _) => Reload(_selectedPath)),
            StudioUi.Button("New Card", (_, _) => ShowCreate()),
            StudioUi.Button("New Trick & Card", (_, _) => ShowCreate(true), true));
        Reload();
        if (shopMode && !ProjectSection.ShopEditingAvailable)
        {
            _shell.IsEnabled = false;
            _shell.ToolTip = "Shop editing is unavailable in this release.";
            var releaseLayout = new Grid();
            releaseLayout.SetResourceReference(Panel.BackgroundProperty, "ProjectManagerCardBrush");
            releaseLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            releaseLayout.RowDefinitions.Add(new RowDefinition());
            var notice = new TextBlock { Text = "Shop disabled for this release.",
                FontWeight = FontWeights.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            notice.SetResourceReference(TextBlock.ForegroundProperty, "WindowTextBrush");
            var banner = new Border { Child = notice, Padding = new Thickness(20, 12, 20, 12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(219, 166, 61)), BorderThickness = new Thickness(0, 0, 0, 2) };
            banner.SetResourceReference(Border.BackgroundProperty, "ProjectManagerFieldBrush");
            releaseLayout.Children.Add(banner);
            Content = null;
            Grid.SetRow(_shell, 1);
            releaseLayout.Children.Add(_shell);
            Content = releaseLayout;
            _status("Shop editing is unavailable in this release.");
        }
    }

    private IEnumerable<string> CardFiles()
    {
        foreach (var folder in new[] { "custom_upgrades", "custom_tricks" })
        {
            var full = Path.Combine(_root, folder);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.EnumerateFiles(full, "*.xml", SearchOption.AllDirectories)
                )
            {
                try
                {
                    if (XDocument.Load(file).Root?.Name.LocalName is not ("CustomCard" or "CustomTrick")) continue;
                }
                catch { continue; }
                yield return file;
            }
        }
    }

    private void Reload(string? select = null)
    {
        var files = CardFiles().Where(file =>
        {
            if (string.IsNullOrWhiteSpace(_search.Text)) return true;
            try
            {
                var root = XDocument.Load(file).Root;
                return new[] { (string?)root?.Attribute("VisualName"), (string?)root?.Attribute("CardName"),
                    (string?)root?.Attribute("Name"), Path.GetFileNameWithoutExtension(file) }
                    .Any(value => value?.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) == true);
            }
            catch { return false; }
        }).OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList();
        _catalogue.ItemsSource = files;
        _catalogue.ItemTemplate = FileTemplate();
        _catalogue.SelectedItem = select is not null && files.Contains(select) ? select : files.FirstOrDefault();
        if (files.Count == 0)
        {
            _details.Children.Clear();
            _details.Children.Add(new TextBlock { Text = "No custom cards yet.", Margin = new Thickness(26, 35, 0, 0), Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        }
    }

    private DataTemplate FileTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(ContentControl));
        factory.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding { Converter = new CardNameConverter(this) });
        template.VisualTree = factory;
        return template;
    }

    private void LoadSelected()
    {
        _selectedPath = _catalogue.SelectedItem as string;
        _details.Children.Clear();
        _inputs.Clear();
        _levelInputs.Clear();
        _document = null;
        if (_selectedPath is null) return;
        try
        {
            _document = XDocument.Load(_selectedPath, LoadOptions.PreserveWhitespace);
            var card = _document.Root;
            if (card is null || card.Name.LocalName is not ("CustomCard" or "CustomTrick")) throw new InvalidDataException("Invalid card package.");
            foreach (var pair in new Dictionary<string, string> {
                ["VisualName"] = (string?)card.Attribute("Name") ?? Path.GetFileName(Path.GetDirectoryName(_selectedPath))!,
                ["Description"] = "", ["CardName"] = ((string?)card.Attribute("Name") ?? "Card") + "_1",
                ["Price"] = "1100", ["Weight"] = "1250", ["SetupMin"] = "0", ["SetupMax"] = "99",
                ["CardType"] = card.Name.LocalName == "CustomTrick" ? "Stunts" : "Passive", ["MaxLevel"] = "5",
                ["Group"] = card.Name.LocalName == "CustomTrick" ? "CustomTricks" : "CustomCards", ["EffectID"] = "None",
                ["Slot"] = "Stunts", ["Category"] = "Custom" })
                if (card.Attribute(pair.Key) is null) card.SetAttributeValue(pair.Key, pair.Value);
            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 1050 };
            if (_shopMode) BuildShop(panel, card);
            else BuildUpgrades(panel, card);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            bar.Children.Add(StudioUi.Button("Revert", (_, _) => LoadSelected()));
            bar.Children.Add(StudioUi.Button("Save Card", (_, _) => Save(), true));
            panel.Children.Add(bar);
            _details.Children.Add(panel);
        }
        catch (Exception ex) { _status($"Could not open card: {ex.Message}"); }
    }

    private void BuildShop(StackPanel panel, XElement card)
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(292) });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var preview = new Grid { Height = 285, Width = 265, Background = StudioUi.Resource("ProjectManagerArtworkBrush"), ClipToBounds = true };
        top.Children.Add(new Border { Child = preview, BorderBrush = StudioUi.Resource("ProjectManagerEdgeBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        var identity = new StackPanel();
        identity.Children.Add(new TextBlock { Text = "Player-facing card", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        identity.Children.Add(new TextBlock { Text = "Exactly what appears in Vector 2", FontSize = 11, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 12) });
        AddField(identity, card, "VisualName", "Display name");
        AddField(identity, card, "Description", "Description");
        _inputs["Description"].AcceptsReturn = true;
        _inputs["Description"].TextWrapping = TextWrapping.Wrap;
        _inputs["Description"].Height = 55;
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var artFields = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        artFields.Children.Add(StudioUi.Label("Artwork"));
        var artworkFiles = new List<string> { "" };
        artworkFiles.AddRange(ArtworkFiles());
        var currentImage = (string?)card.Attribute("Image") ?? "";
        if (currentImage.Length > 0 && !artworkFiles.Contains(currentImage, StringComparer.OrdinalIgnoreCase)) artworkFiles.Add(currentImage);
        _artwork = new ComboBox { ItemsSource = artworkFiles, SelectedItem = currentImage, Margin = new Thickness(0, 4, 0, 12) };
        artFields.Children.Add(_artwork);
        choices.Children.Add(artFields);
        var rarityFields = new StackPanel();
        rarityFields.Children.Add(StudioUi.Label("Rarity"));
        _rarity = new ComboBox { Margin = new Thickness(0, 4, 0, 12), ItemsSource = new[] { "Common", "Rare", "Epic" }, SelectedIndex = Math.Clamp(Parse(card, "Rarity", 1) - 1, 0, 2) };
        rarityFields.Children.Add(StudioUi.Segments(_rarity));
        Grid.SetColumn(rarityFields, 1); choices.Children.Add(rarityFields);
        identity.Children.Add(choices);
        _enabled = new CheckBox { Content = "Available in the shop", IsChecked = (string?)card.Attribute("ShopEnabled") != "0", Margin = new Thickness(0, 4, 0, 10) };
        _enabled.SetResourceReference(StyleProperty, "StudioSwitch");
        identity.Children.Add(_enabled);
        Grid.SetColumn(identity, 1);
        top.Children.Add(identity);
        panel.Children.Add(top);

        var groups = new Grid { Margin = new Thickness(0, 30, 0, 0) };
        for (var i = 0; i < 3; i++) groups.ColumnDefinitions.Add(new ColumnDefinition());
        AddGroup(groups, 0, "Pricing", card, ["Price", "Weight"]);
        AddGroup(groups, 1, "Availability", card, ["SetupMin", "SetupMax", "Group"]);
        AddGroup(groups, 2, "Gameplay", card, ["CardName", "Category", "Slot"]);
        panel.Children.Add(groups);
        var gameplay = (StackPanel)groups.Children[2];
        gameplay.Children.Insert(3, StudioUi.Label("Card type"));
        _cardType = new ComboBox { Margin = new Thickness(0, 4, 0, 12), ItemsSource = new[] { "Passive", "Stunts", "Notes", "StoryItems" }, SelectedItem = (string?)card.Attribute("CardType") ?? "Passive" };
        gameplay.Children.Insert(4, _cardType);
        AddField(gameplay, card, "EffectID", "Player effect");
        var effectText = _inputs["EffectID"];
        gameplay.Children.Remove(effectText);
        var effectChoices = GameplayEffectCatalog.CardEffects.ToList();
        if (GameplayEffectCatalog.Find(effectText.Text) is null)
            effectChoices.Add(new GameplayEffect(effectText.Text, "Existing custom effect: " + effectText.Text, "The current runtime does not execute this effect.", new Dictionary<string, string>()));
        var effects = new ComboBox { ItemsSource = effectChoices, SelectedItem = effectChoices.First(item => item.Id.Equals(effectText.Text, StringComparison.OrdinalIgnoreCase)), Margin = new Thickness(0, 4, 0, 4) };
        var effectDescription = new TextBlock { FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 12) };
        effects.SelectionChanged += (_, _) => { if (effects.SelectedItem is GameplayEffect selected) { effectText.Text = selected.Id; effectDescription.Text = selected.Description; } };
        gameplay.Children.Add(effects);
        gameplay.Children.Add(effectDescription);
        effectDescription.Text = (effects.SelectedItem as GameplayEffect)?.Description ?? "";
        void UpdateEffectVisibility() => effects.Visibility = effectDescription.Visibility =
            _cardType.SelectedItem as string == "Passive" ? Visibility.Visible : Visibility.Collapsed;
        _cardType.SelectionChanged += (_, _) => UpdateEffectVisibility();
        UpdateEffectVisibility();
        void RefreshPreview(object? sender, EventArgs args) => SetCardPreview(preview,
            _inputs["VisualName"].Text,
            _artwork?.SelectedItem as string,
            _rarity?.SelectedItem as string,
            _inputs["Price"].Text);
        _inputs["VisualName"].TextChanged += (_, _) => RefreshPreview(null, EventArgs.Empty);
        _inputs["Price"].TextChanged += (_, _) => RefreshPreview(null, EventArgs.Empty);
        _artwork.SelectionChanged += (_, _) => RefreshPreview(null, EventArgs.Empty);
        _rarity.SelectionChanged += (_, _) => RefreshPreview(null, EventArgs.Empty);
        RefreshPreview(null, EventArgs.Empty);
    }

    private void BuildUpgrades(StackPanel panel, XElement card)
    {
        panel.Children.Add(new TextBlock { Text = (string?)card.Attribute("VisualName") ?? "Card", FontSize = 21, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Upgrade path", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 10) });
        AddField(panel, card, "EffectID", "Player effect");
        AddField(panel, card, "MaxLevel", "Maximum level");
        var maxField = _inputs["MaxLevel"];
        panel.Children.Remove(maxField);
        panel.Children.Add(StudioUi.Number(maxField, 1, 10));
        var levels = new StackPanel();
        panel.Children.Add(levels);
        var defaults = new[] { (2,100), (3,120), (5,145), (7,175), (9,210), (12,250), (15,300), (19,360), (24,430), (30,510) };
        for (var number = 1; number <= 10; number++)
        {
            var existing = card.Elements("Level").FirstOrDefault(level => Parse(level, "Number", 0) == number);
            if (existing is null) card.Add(new XElement("Level", new XAttribute("Number", number), new XAttribute("Cards", defaults[number - 1].Item1), new XAttribute("Points", defaults[number - 1].Item2)));
        }
        void RenderLevels()
        {
            foreach (var input in _levelInputs) input.Element.SetAttributeValue(input.Attribute, input.Input.Text);
            _levelInputs.Clear(); levels.Children.Clear();
            var count = int.TryParse(maxField.Text, out var value) ? Math.Clamp(value, 1, 10) : 5;
            foreach (var level in card.Elements("Level").Where(level => Parse(level, "Number", 0) <= count).OrderBy(level => Parse(level, "Number", 0)))
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
                var number = Parse(level, "Number", 1);
                row.Children.Add(new TextBlock { Text = $"Level {number}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var index = 1;
                foreach (var attribute in level.Attributes().Where(attribute => attribute.Name.LocalName != "Number"))
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    var field = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                    field.Children.Add(StudioUi.Label(attribute.Name.LocalName switch { "Cards" => "Copies needed", "Points" => "Power", _ => attribute.Name.LocalName }));
                    var input = StudioUi.Field(attribute.Value);
                    _levelInputs.Add((level, attribute.Name.LocalName, input));
                    field.Children.Add(attribute.Name.LocalName is "Cards" or "Points" ? StudioUi.Number(input, attribute.Name.LocalName == "Cards" ? 1 : 0, attribute.Name.LocalName == "Cards" ? 99 : 100000) : input);
                    Grid.SetColumn(field, index++); row.Children.Add(field);
                }
                levels.Children.Add(row);
            }
        }
        maxField.TextChanged += (_, _) => RenderLevels();
        RenderLevels();
        var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var property = StudioUi.Field(); property.Width = 180; property.ToolTip = "Effect property name";
        add.Children.Add(property);
        add.Children.Add(StudioUi.Button("Add Property", (_, _) => {
            try {
                System.Xml.XmlConvert.VerifyNCName(property.Text.Trim());
                var name = property.Text.Trim();
                if (new[] { "Number", "Cards", "Points" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
                foreach (var level in card.Elements("Level")) if (level.Attribute(name) is null) level.SetAttributeValue(name, "0");
                property.Clear(); RenderLevels();
            } catch { _status("Enter a valid effect property name."); }
        }));
        panel.Children.Add(add);
    }

    private void AddGroup(Grid grid, int column, string title, XElement card, string[] attributes)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        group.Children.Add(StudioUi.Heading(title, column == 0 ? "\uE8C7" : column == 1 ? "\uE9E9" : "\uE7FC", column == 0 ? Color.FromRgb(41,183,80) : column == 1 ? Color.FromRgb(20,132,245) : Color.FromRgb(236,132,55)));
        foreach (var attribute in attributes)
        {
            AddField(group, card, attribute, attribute switch { "Weight" => "Selection weight", "SetupMin" => "Minimum progression", "SetupMax" => "Maximum progression", "Group" => "Shop group", "CardName" => "Card ID", "Slot" => "Equipment slot", _ => attribute });
            if (attribute is "Price" or "Weight" or "SetupMin" or "SetupMax")
            {
                var input = _inputs[attribute]; group.Children.Remove(input);
                group.Children.Add(StudioUi.Number(input, attribute == "Weight" ? 1 : 0, attribute.StartsWith("Setup") ? 999 : 100000, attribute == "Price" ? 100 : attribute == "Weight" ? 25 : 1));
            }
        }
        Grid.SetColumn(group, column);
        grid.Children.Add(group);
    }

    private void AddField(Panel panel, XElement card, string attribute, string label)
    {
        panel.Children.Add(StudioUi.Label(label));
        var input = StudioUi.Field((string?)card.Attribute(attribute) ?? "");
        panel.Children.Add(input);
        _inputs[attribute] = input;
    }

    private static int Parse(XElement element, string name, int fallback) => int.TryParse((string?)element.Attribute(name), out var value) ? value : fallback;

    private IEnumerable<string> ArtworkFiles()
    {
        foreach (var relative in new[] { "custom_textures", "custom_backgrounds" })
        {
            var folder = Path.Combine(_root, relative);
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(file).ToLowerInvariant())))
                yield return Path.GetFileName(file);
        }
    }

    private string? FindArtwork(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var relative in new[] { "custom_textures", "custom_backgrounds" })
        {
            var folder = Path.Combine(_root, relative);
            if (!Directory.Exists(folder)) continue;
            var file = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .FirstOrDefault(file => Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (file is not null) return file;
        }
        return null;
    }

    private void SetCardPreview(Grid preview, string name, string? imageName, string? rarity, string price)
    {
        preview.Children.Clear();
        var image = FindArtwork(imageName);
        if (image is not null)
        {
            try { preview.Children.Add(new Image { Source = new BitmapImage(new Uri(image)), Stretch = Stretch.UniformToFill }); }
            catch { image = null; }
        }
        if (image is null)
            preview.Children.Add(new TextBlock { Text = "\uE7FC", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 35,
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(185, 20, 26, 36)) };
        labels.Children.Add(new TextBlock { Text = (rarity ?? "Common").ToUpperInvariant(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 9, Margin = new Thickness(9, 6, 9, 0) });
        labels.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(9, 2, 9, 0) });
        labels.Children.Add(new TextBlock { Text = $"{price} credits", Foreground = Brushes.White, FontSize = 10, Margin = new Thickness(9, 4, 9, 8) });
        preview.Children.Add(labels);
    }

    private void Save()
    {
        if (_document?.Root is not { } card || _selectedPath is null) return;
        try
        {
            static int InRange(string value, string label, int minimum, int maximum)
            {
                if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
                    throw new InvalidDataException($"{label} must be between {minimum} and {maximum}.");
                return parsed;
            }
            if (_shopMode)
            {
                InRange(_inputs["Price"].Text, "Price", 0, 100000);
                InRange(_inputs["Weight"].Text, "Selection weight", 1, 100000);
                var minimum = InRange(_inputs["SetupMin"].Text, "Minimum progression", 0, 999);
                var maximum = InRange(_inputs["SetupMax"].Text, "Maximum progression", 0, 999);
                if (maximum < minimum) throw new InvalidDataException("Maximum progression must be at least the minimum.");
                if (string.IsNullOrWhiteSpace(_inputs["VisualName"].Text)) throw new InvalidDataException("Display name is required.");
            }
            else
            {
                InRange(_inputs["MaxLevel"].Text, "Maximum level", 1, 10);
                foreach (var entry in _levelInputs)
                {
                    if (entry.Attribute == "Cards") InRange(entry.Input.Text, "Copies", 1, 99);
                    if (entry.Attribute == "Points") InRange(entry.Input.Text, "Power", 0, 100000);
                }
            }
            var previousEffect = (string?)card.Attribute("EffectID") ?? "None";
            foreach (var pair in _inputs) card.SetAttributeValue(pair.Key, pair.Value.Text.Trim());
            if (_artwork is not null) card.SetAttributeValue("Image", _artwork.SelectedItem as string ?? "");
            if (_rarity is not null) card.SetAttributeValue("Rarity", _rarity.SelectedIndex + 1);
            if (_enabled is not null) card.SetAttributeValue("ShopEnabled", _enabled.IsChecked == true ? "1" : "0");
            if (_cardType is not null) card.SetAttributeValue("CardType", _cardType.SelectedItem as string);
            foreach (var entry in _levelInputs) entry.Element.SetAttributeValue(entry.Attribute, entry.Input.Text.Trim());
            if (!_shopMode) foreach (var level in card.Elements("Level").Where(level => Parse(level, "Number", 0) > Parse(card, "MaxLevel", 5)).ToList()) level.Remove();
            if (_shopMode && _inputs.TryGetValue("EffectID", out var effectInput) &&
                !string.Equals(previousEffect, effectInput.Text, StringComparison.OrdinalIgnoreCase))
                GameplayEffectCatalog.ConfigureLevels(card, effectInput.Text);
            var temporary = _selectedPath + ".tmp";
            _document.Save(temporary);
            File.Move(temporary, _selectedPath, true);
            _status($"Saved {Path.GetFileName(_selectedPath)}.");
            Reload(_selectedPath);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void ShowCreate(bool trick = false)
    {
        if (trick)
        {
            Content = new CustomTrickCreatorView(_root, _status, () => { Content = _shell; Reload(); });
            return;
        }
        _details.Children.Clear();
        var panel = new StackPanel { MaxWidth = 780, Margin = new Thickness(26), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "Create a Custom Card", FontSize = 23, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
        panel.Children.Add(new TextBlock { Text = "Add an upgrade, story note, or story item to the shop.", Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 18) });
        var type = new ComboBox { ItemsSource = new[] { "Passive", "Notes", "StoryItems" }, SelectedIndex = 0 };
        panel.Children.Add(StudioUi.Label("Card type")); panel.Children.Add(type);
        panel.Children.Add(StudioUi.Heading("Identity", "\uE8A1", Color.FromRgb(38, 127, 229)));
        panel.Children.Add(StudioUi.Label("Card ID"));
        var id = StudioUi.Field(); panel.Children.Add(id);
        panel.Children.Add(StudioUi.Label("Player-facing name"));
        var name = StudioUi.Field(); panel.Children.Add(name);
        panel.Children.Add(StudioUi.Label("Description"));
        var description = StudioUi.Field(); description.AcceptsReturn = true; description.Height = 70; description.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(description);
        panel.Children.Add(StudioUi.Heading("Presentation", "\uE91B", Color.FromRgb(38, 127, 229)));
        panel.Children.Add(StudioUi.Label("Artwork"));
        var image = new ComboBox { ItemsSource = new[] { "" }.Concat(ArtworkFiles().Distinct(StringComparer.OrdinalIgnoreCase)).ToList(), SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 12) };
        panel.Children.Add(image);
        panel.Children.Add(StudioUi.Label("Category"));
        var category = StudioUi.Field("Custom"); panel.Children.Add(category);
        panel.Children.Add(StudioUi.Heading("Gameplay", "\uE7FC", Color.FromRgb(236, 132, 55)));
        panel.Children.Add(StudioUi.Label("Equipment slot"));
        var slot = new ComboBox { ItemsSource = new[] { "Head", "Torso", "Hands", "Belt", "Legs" }, SelectedItem = "Torso", Margin = new Thickness(0, 4, 0, 12) };
        panel.Children.Add(slot);
        panel.Children.Add(StudioUi.Label("Player effect"));
        var effect = new ComboBox { ItemsSource = GameplayEffectCatalog.CardEffects, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 5) };
        panel.Children.Add(effect);
        var effectHelp = new TextBlock { Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(effectHelp);
        effect.SelectionChanged += (_, _) => effectHelp.Text = (effect.SelectedItem as GameplayEffect)?.Description ?? "";
        effectHelp.Text = GameplayEffectCatalog.CardEffects[0].Description;
        type.SelectionChanged += (_, _) =>
        {
            var passive = type.SelectedItem as string == "Passive";
            slot.Visibility = effect.Visibility = effectHelp.Visibility = passive ? Visibility.Visible : Visibility.Collapsed;
            category.Text = (type.SelectedItem as string) switch { "Notes" => "CustomNotes", "StoryItems" => "StoryItems", _ => "Custom" };
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(StudioUi.Button("Cancel", (_, _) => Reload(_selectedPath)));
        actions.Children.Add(StudioUi.Button("Create Card", (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException("Player-facing name is required.");
                var stableId = ProjectTemplateService.SafeId(id.Text);
                if (stableId.Length == 0) throw new InvalidDataException("Enter a stable card ID.");
                var path = new ProjectTemplateService().Create(new Vector2Project { RootPath = _root },
                    ProjectSection.All.Single(item => item.Name == "Shop"), stableId);
                var doc = XDocument.Load(path);
                var card = doc.Root!;
                var cardType = type.SelectedItem as string ?? "Passive";
                card.SetAttributeValue("VisualName", name.Text.Trim());
                card.SetAttributeValue("Description", description.Text.Trim());
                card.SetAttributeValue("Image", image.SelectedItem as string ?? "");
                card.SetAttributeValue("CardType", cardType);
                card.SetAttributeValue("Category", category.Text.Trim());
                card.SetAttributeValue("Slot", cardType == "Passive" ? slot.SelectedItem as string ?? "Torso" : cardType);
                card.SetAttributeValue("EffectID", cardType == "Passive" ? (effect.SelectedItem as GameplayEffect)?.Id ?? "None" : "None");
                card.SetAttributeValue("ShopEnabled", cardType == "Passive" ? "1" : "0");
                card.SetAttributeValue("MaxLevel", cardType == "Passive" ? "5" : "1");
                if (cardType != "Passive") card.Elements("Level").Skip(1).Remove();
                GameplayEffectCatalog.ConfigureLevels(card, (string?)card.Attribute("EffectID") ?? "None");
                doc.Save(path);
                Reload(path);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status(ex.Message); }
        }, true));
        panel.Children.Add(actions);
        _details.Children.Add(panel);
        id.Focus();
    }

    private void Delete()
    {
        if (_selectedPath is null) return;
        if (MessageBox.Show("Delete this card package and its associated files?", "Delete card", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var package = Path.GetDirectoryName(_selectedPath)!;
            var root = Path.GetFullPath(_root);
            if (!Path.GetFullPath(package).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Card is outside this project.");
            var library = Path.GetDirectoryName(package);
            if (library is null || !(Path.GetFileName(library).Equals("custom_upgrades", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(library).Equals("custom_tricks", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This card is not in its own package. Delete its file manually.");
            Directory.Delete(package, true);
            Reload();
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private sealed class CardNameConverter(CommerceStudioView owner) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try {
                var card = XDocument.Load((string)value).Root!;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(47) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var art = new Grid { Width = 38, Height = 42, Background = StudioUi.Resource("ProjectManagerArtworkBrush") };
                var path = owner.FindArtwork((string?)card.Attribute("Image"));
                if (path is not null) art.Children.Add(new Image { Source = new BitmapImage(new Uri(path)), Stretch = Stretch.UniformToFill });
                else art.Children.Add(new TextBlock { Text = "\uE7FC", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                row.Children.Add(art);
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                labels.Children.Add(new TextBlock { Text = (string?)card.Attribute("VisualName") ?? Path.GetFileName(Path.GetDirectoryName((string)value)), FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                labels.Children.Add(new TextBlock { Text = $"{new[] { "Common", "Rare", "Epic" }[Math.Clamp(Parse(card, "Rarity", 1) - 1, 0, 2)]} · {Parse(card, "Price", 1100):N0} credits", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0,3,0,0) });
                Grid.SetColumn(labels, 1); row.Children.Add(labels);
                return row;
            }
            catch { return Path.GetFileName((string)value); }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
