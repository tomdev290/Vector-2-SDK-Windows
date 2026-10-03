using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Windows first-pass port of the Swift runtime graph/library reconstruction.
/// 
/// Vector 2 library objects are not single sprites. Most "assets" are tiny XML
/// graphs containing Images, Platforms, Triggers, Areas, and nested
/// ObjectReferences. The macOS editor expands those graphs for preview while
/// still exporting the compact ObjectReference. This class gives WPF the same
/// behavior for the common cases, so imported rooms stop turning every advanced
/// object into a sad green placeholder box.
/// </summary>
public static class Vector2LibraryObjectBuilder
{
    private const int MaxDepth = 5;
    private const int MaxPreviewPieces = 5000;
    private const string PreviewCacheVersion = "wpf-runtime-preview-v6";

    public static bool TryReconstructReference(LevelNode reference, string? importPath = null)
        => TryReconstructReference(reference, importPath, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static bool TryReconstructReference(LevelNode reference, string? importPath, IReadOnlyDictionary<string, string> overrides)
    {
        if (reference.Kind is not (LevelNodeKind.ObjectReference or LevelNodeKind.Object or LevelNodeKind.Camera or LevelNodeKind.Coin))
        {
            return false;
        }

        var filename = string.IsNullOrWhiteSpace(reference.Filename) ? reference.Choice : reference.Filename;
        var libraryPath = Vector2Catalog.ResolveLibraryPath(filename, importPath);
        if (string.IsNullOrWhiteSpace(libraryPath)) return false;

        var lookupName = !string.IsNullOrWhiteSpace(reference.OverrideStuntName) ? reference.OverrideStuntName : reference.Name;
        var objectXml = FindLibraryObject(libraryPath, lookupName);
        if (objectXml is null)
        {
            DiagnosticsLog.Warn($"Library object '{lookupName}' was not found in {Path.GetFileName(libraryPath)}.");
            return false;
        }

        reference.Template = string.IsNullOrWhiteSpace(reference.Template) ? "LibraryObject" : reference.Template;
        reference.Filename = Path.GetFileName(libraryPath);
        reference.Choice = Path.GetFileName(libraryPath);
        reference.Children.Clear();
        if (IsPhantomLibrary(libraryPath))
        {
            reference.ImagePath = "";
            reference.VisualOffsetX = 0;
            reference.VisualOffsetY = 0;
        }

        if (IsPhantomLibrary(libraryPath) && TryApplyStuntIconPreview(reference, lookupName, libraryPath))
        {
            ApplyPreviewBounds(reference);
            DiagnosticsLog.Info($"Using stunt icon preview for phantom '{lookupName}'.");
            return true;
        }

        var variables = CollectDefaultVariables(objectXml);
        foreach (var pair in overrides)
        {
            variables[pair.Key] = ResolveString(pair.Value, variables);
        }

        var pieces = new List<LevelNode>();
        CollectLibraryInstancePieces(objectXml, pieces, AffineContext.Identity.Translated(reference.X, reference.Y), reference.Factor, libraryPath, variables, 0);
        foreach (var piece in pieces.Take(MaxPreviewPieces))
        {
            piece.MarkPreviewOnly(true);
            reference.Children.Add(piece);
        }

        ApplyPhantomVisualFallback(reference, lookupName, libraryPath);

        // Browser thumbnails are not runtime artwork; stretching one over a logical trigger
        // creates a misleading stripe beside its real canvas region.
        if (!IsPhantomLibrary(libraryPath) && reference.Children.Count > 0 &&
            !reference.Children.SelectMany(child => child.Flatten()).Any(child => child.Kind == LevelNodeKind.Image))
            reference.ImagePath = "";

        ApplyPreviewBounds(reference);
        ApplyRasterizedPreview(reference, lookupName, libraryPath);
        DiagnosticsLog.Info($"Reconstructed '{reference.Name}' from {Path.GetFileName(libraryPath)} with {reference.Children.Count} preview pieces.");
        return reference.Children.Count > 0;
    }

    public static bool TryReconstructAsset(LevelNode node, TextureAsset asset)
    {
        var sourcePath = string.IsNullOrWhiteSpace(asset.SourcePath) ? asset.FilePath : asset.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        node.Filename = Path.GetFileName(sourcePath);
        node.Choice = node.Filename;

        if (asset.Kind == TextureAssetKind.LibraryObject &&
            !string.IsNullOrWhiteSpace(asset.ClassName) &&
            !asset.ClassName.Equals(asset.Name, StringComparison.OrdinalIgnoreCase))
        {
            var variantName = asset.Name;
            var targetName = asset.ClassName;
            node.Name = targetName;
            var ok = TryReconstructReference(node, sourcePath, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [variantName] = "1"
            });
            node.Name = variantName;
            node.ClassName = targetName;
            return ok;
        }

        return TryReconstructReference(node, sourcePath);
    }

    public static string BuildAssetPreviewImage(TextureAsset asset)
    {
        if (asset.Kind != TextureAssetKind.LibraryObject) return asset.FilePath;

        var sourcePath = string.IsNullOrWhiteSpace(asset.SourcePath) ? asset.FilePath : asset.SourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath)) return asset.FilePath;

        var targetName = !string.IsNullOrWhiteSpace(asset.ClassName) ? asset.ClassName : asset.Name;
        var isVariant = asset.Kind == TextureAssetKind.LibraryObject &&
                        !targetName.Equals(asset.Name, StringComparison.OrdinalIgnoreCase);
        var node = new LevelNode
        {
            Name = targetName,
            Kind = LevelNodeKind.ObjectReference,
            X = 0,
            Y = 0,
            Width = 120,
            Height = 120,
            Filename = Path.GetFileName(sourcePath),
            Choice = Path.GetFileName(sourcePath),
            ClassName = asset.ClassName,
            Template = "LibraryObject",
            Variant = "Resolved",
            Tag = "ObjectReference"
        };

        var ok = isVariant
            ? TryReconstructReference(node, sourcePath, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [asset.Name] = "1"
            })
            : TryReconstructReference(node, sourcePath);
        if (!ok) return asset.FilePath;
        if (File.Exists(node.ImagePath)) return node.ImagePath;

        if (Path.GetFileName(sourcePath).Equals("phantoms.xml", StringComparison.OrdinalIgnoreCase) &&
            TryResolveStuntIcon(asset.Name, out _, out var stuntIconPath))
        {
            return stuntIconPath;
        }

        var runtimeShape = node.Children
            .SelectMany(child => child.Flatten())
            .FirstOrDefault(child => !child.IsHidden && child.Kind is LevelNodeKind.Area or LevelNodeKind.Trigger);
        if (runtimeShape is null) return asset.FilePath;

        return BuildRunnerPreviewImage(
            asset,
            string.IsNullOrWhiteSpace(runtimeShape.Name) ? asset.Name : runtimeShape.Name,
            runtimeShape.Kind == LevelNodeKind.Area);
    }

    public static string BuildPlaceholderPreviewImage(TextureAsset asset)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "Vector2LevelEditorLibraryPreviewsWpf");
            Directory.CreateDirectory(root);
            var hash = StableHash($"{asset.Kind}:{asset.Group}:{asset.Category}:{asset.Name}:{asset.SourcePath}:{asset.InstanceKey}");
            var path = Path.Combine(root, $"placeholder_{hash}.png");
            if (File.Exists(path)) return path;

            const int width = 320;
            const int height = 220;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bg = new LinearGradientBrush(Color.FromRgb(244, 247, 251), Color.FromRgb(222, 229, 238), 90);
                dc.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(Color.FromRgb(190, 201, 214)), 1), new Rect(0.5, 0.5, width - 1, height - 1), 14, 14);

                var accent = asset.Kind switch
                {
                    TextureAssetKind.Prefab => Color.FromRgb(75, 121, 190),
                    TextureAssetKind.LibraryObject => Color.FromRgb(82, 155, 124),
                    _ => Color.FromRgb(115, 115, 160)
                };
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)), null, new Rect(28, 28, 264, 118), 10, 10);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(130, accent.R, accent.G, accent.B)), null, new Rect(28, 154, 264, 6));

                var initials = Initials(asset.Name);
                var title = new FormattedText(initials, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 42, new SolidColorBrush(Color.FromRgb(45, 55, 72)), 1.0);
                dc.DrawText(title, new Point((width - title.Width) / 2, 66));

                var name = new FormattedText(TrimText(asset.Name, 30), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 18, new SolidColorBrush(Color.FromRgb(31, 41, 55)), 1.0)
                {
                    MaxTextWidth = width - 36,
                    TextAlignment = TextAlignment.Center
                };
                dc.DrawText(name, new Point(18, 166));

                var category = new FormattedText(TrimText(asset.Category, 34), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromRgb(92, 103, 115)), 1.0)
                {
                    MaxTextWidth = width - 36,
                    TextAlignment = TextAlignment.Center
                };
                dc.DrawText(category, new Point(18, 192));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not create placeholder preview for '{asset.Name}': {ex.Message}");
            return "";
        }
    }

    public static string BuildRunnerPreviewImage(TextureAsset asset, string runnerName, bool isArea)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "Vector2LevelEditorLibraryPreviewsWpf");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"runner_v2_{StableHash($"{asset.SourcePath}:{runnerName}:{isArea}")}.png");
            if (File.Exists(path)) return path;

            const int width = 320;
            const int height = 220;
            var fill = isArea ? Color.FromArgb(95, 255, 90, 105) : Color.FromArgb(90, 255, 190, 0);
            var stroke = isArea ? Colors.IndianRed : Colors.Orange;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 253)), null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(stroke), 3), new Rect(20, 20, width - 40, height - 40));
                var label = new FormattedText(
                    string.IsNullOrWhiteSpace(runnerName) ? (isArea ? "Area" : "Trigger") : runnerName,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"),
                    22,
                    Brushes.Black,
                    1.0)
                {
                    MaxTextWidth = width - 56,
                    TextAlignment = TextAlignment.Center
                };
                dc.DrawText(label, new Point(28, (height - label.Height) / 2));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not create runner preview for '{runnerName}': {ex.Message}");
            return "";
        }
    }

    private static string Initials(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Split(text, @"[^A-Za-z0-9]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Take(2)
            .ToArray();
        if (words.Length == 0) return "?";
        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0])));
    }

    private static string TrimText(string text, int max)
        => string.IsNullOrWhiteSpace(text) || text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "...";

    private static XElement? FindLibraryObject(string libraryPath, string name)
    {
        try
        {
            var document = XDocument.Load(libraryPath, LoadOptions.PreserveWhitespace);
            var exact = TopLevelLibraryObjects(document)
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;

            return TopLevelLibraryObjects(document)
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("Class"), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not load library XML '{libraryPath}': {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<XElement> TopLevelLibraryObjects(XDocument document)
    {
        var root = document.Root;
        if (root is null) yield break;

        var containers = root.Elements("Objects").ToList();
        if (containers.Count == 0)
        {
            containers.Add(root);
        }

        foreach (var container in containers)
        {
            foreach (var element in container.Elements("Object"))
            {
                yield return element;
            }
        }
    }

    private readonly record struct AffineContext(double X, double Y, double XX, double XY, double YX, double YY)
    {
        public static AffineContext Identity { get; } = new(0, 0, 1, 0, 0, 1);

        public AffineContext Translated(double x, double y)
            => new(X + (x * XX) + (y * YX), Y + (x * XY) + (y * YY), XX, XY, YX, YY);

        public AffineContext Combined(double x, double y, double a, double b, double c, double d, double tx, double ty)
        {
            var origin = Point(x + tx, y + ty);
            return new(
                origin.X,
                origin.Y,
                (a * XX) + (b * YX),
                (a * XY) + (b * YY),
                (c * XX) + (d * YX),
                (c * XY) + (d * YY));
        }

        public (double X, double Y) Point(double localX, double localY)
            => (X + (localX * XX) + (localY * YX), Y + (localX * XY) + (localY * YY));

        public (double X, double Y) Vector(double localX, double localY)
            => ((localX * XX) + (localY * YX), (localX * XY) + (localY * YY));
    }

    private static void CollectLibraryInstancePieces(
        XElement root,
        List<LevelNode> pieces,
        AffineContext context,
        string inheritedFactor,
        string libraryPath,
        IReadOnlyDictionary<string, string> variables,
        int depth)
    {
        if (depth > MaxDepth || pieces.Count >= MaxPreviewPieces) return;
        var matrix = ReadMatrix(root);
        var current = context.Combined(0, 0, matrix.A, matrix.B, matrix.C, matrix.D, matrix.Tx, matrix.Ty);
        CollectContentPieces(root, pieces, current, inheritedFactor, libraryPath, variables, depth);
    }

    private static void CollectObjectPieces(
        XElement root,
        List<LevelNode> pieces,
        AffineContext context,
        string inheritedFactor,
        string libraryPath,
        IReadOnlyDictionary<string, string> variables,
        int depth)
    {
        if (depth > MaxDepth || pieces.Count >= MaxPreviewPieces) return;
        var matrix = ReadMatrix(root);
        var current = context.Combined(IntAttr(root, "X", 0, variables), IntAttr(root, "Y", 0, variables), matrix.A, matrix.B, matrix.C, matrix.D, matrix.Tx, matrix.Ty);
        CollectContentPieces(root, pieces, current, inheritedFactor, libraryPath, variables, depth);
    }

    private static void CollectContentPieces(
        XElement root,
        List<LevelNode> pieces,
        AffineContext context,
        string inheritedFactor,
        string libraryPath,
        IReadOnlyDictionary<string, string> variables,
        int depth)
    {
        if (depth > MaxDepth || pieces.Count >= MaxPreviewPieces) return;

        var sourceElements = root.Elements("Content").Elements().ToList();
        if (sourceElements.Count == 0)
        {
            sourceElements = root.Elements().Where(e => e.Name.LocalName is not "Properties").ToList();
        }

        foreach (var element in sourceElements)
        {
            if (pieces.Count >= MaxPreviewPieces) return;
            if (!IsElementEnabled(element, variables)) continue;

            var tag = element.Name.LocalName;
            var factor = (string?)element.Attribute("Factor") ?? inheritedFactor;

            if (tag == "Object")
            {
                CollectObjectPieces(element, pieces, context, factor, libraryPath, variables, depth + 1);
                continue;
            }

            if (tag == "ObjectReference")
            {
                var filename = ResolveString((string?)element.Attribute("Filename") ?? "", variables);
                var referencedPath = Vector2Catalog.ResolveLibraryPath(filename, libraryPath);
                var childOverrides = CollectOverrideVariables(element, variables);
                var referencedName = ResolveString((string?)element.Attribute("Name") ?? "", variables);
                if (childOverrides.TryGetValue("StuntName", out var stuntName) && referencedName.Equals("Stunt", StringComparison.OrdinalIgnoreCase))
                {
                    referencedName = stuntName;
                }
                var referencedObject = string.IsNullOrWhiteSpace(referencedPath) ? null : FindLibraryObject(referencedPath, referencedName);
                if (referencedObject is not null)
                {
                    var childVariables = CollectDefaultVariables(referencedObject);
                    foreach (var pair in childOverrides)
                    {
                        childVariables[pair.Key] = ResolveString(pair.Value, variables);
                    }
                    var matrix = ReadMatrix(element);
                    var referenceContext = context.Combined(IntAttr(element, "X", 0, variables), IntAttr(element, "Y", 0, variables), matrix.A, matrix.B, matrix.C, matrix.D, matrix.Tx, matrix.Ty);
                    CollectLibraryInstancePieces(referencedObject, pieces, referenceContext, factor, referencedPath, childVariables, depth + 1);
                }
                continue;
            }

            var point = context.Point(IntAttr(element, "X", 0, variables), IntAttr(element, "Y", 0, variables));
            var x = (int)Math.Round(point.X);
            var y = (int)Math.Round(point.Y);
            var node = tag switch
            {
                "Image" => ImageNode(element, x, y, factor, variables, context),
                "Platform" => BasicNode(element, LevelNodeKind.Platform, x, y, factor, variables),
                "Trapezoid" => TrapezoidNode(element, x, y, factor, variables),
                "Trigger" => BasicNode(element, LevelNodeKind.Trigger, x, y, factor, variables),
                "Area" => BasicNode(element, LevelNodeKind.Area, x, y, factor, variables),
                "Comment" => LibraryCommentVisualNode(element, x, y, factor, variables),
                "In" => BasicNode(element, LevelNodeKind.GateIn, x, y, factor, variables),
                "Out" => BasicNode(element, LevelNodeKind.GateOut, x, y, factor, variables),
                "Camera" => BasicNode(element, LevelNodeKind.Camera, x, y, factor, variables),
                "Dynamic" or "DynamicTrigger" => BasicNode(element, LevelNodeKind.Dynamic, x, y, factor, variables),
                _ => null
            };

            if (node is not null) pieces.Add(node);
        }
    }

    private static bool IsElementEnabled(XElement element, IReadOnlyDictionary<string, string> variables)
    {
        var value = element
            .Elements("Properties")
            .Elements("Static")
            .Elements("Enable")
            .Select(enable => (string?)enable.Attribute("Value") ?? "")
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value)) return true;

        var resolved = ResolveString(value, variables).Trim();
        if (string.IsNullOrWhiteSpace(resolved)) return true;
        if (resolved.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            resolved.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            resolved.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static LevelNode BasicNode(XElement element, LevelNodeKind kind, int x, int y, string factor, IReadOnlyDictionary<string, string> variables)
        => BasicNode(element, kind, x, y, factor, variables, null);

    private static LevelNode BasicNode(XElement element, LevelNodeKind kind, int x, int y, string factor, IReadOnlyDictionary<string, string> variables, AffineContext? transformed)
    {
        var width = Math.Max(1, IntAttr(element, "Width", DefaultWidth(kind), variables));
        var height = Math.Max(1, IntAttr(element, "Height", DefaultHeight(kind), variables));
        if (transformed is { } context)
        {
            var basisX = context.Vector(width, 0);
            var basisY = context.Vector(0, height);
            var corners = new[]
            {
                context.Point(0, 0),
                context.Point(width, 0),
                context.Point(width, height),
                context.Point(0, height)
            };
            x = (int)Math.Round(corners.Min(p => p.X));
            y = (int)Math.Round(corners.Min(p => p.Y));
            width = Math.Max(1, (int)Math.Round(corners.Max(p => p.X) - corners.Min(p => p.X)));
            height = Math.Max(1, (int)Math.Round(corners.Max(p => p.Y) - corners.Min(p => p.Y)));
        }

        var node = new LevelNode
        {
            Kind = kind,
            Name = ResolveString((string?)element.Attribute("Name") ?? (string?)element.Attribute("Text") ?? element.Name.LocalName, variables),
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Factor = factor,
            SortingLayer = ResolveString((string?)element.Attribute("Layer") ?? (kind is LevelNodeKind.Platform or LevelNodeKind.Trapezoid ? "Collision" : "Default"), variables),
            Tag = kind.ToString(),
            Template = element.Name.LocalName,
            Variant = ResolveString((string?)element.Attribute("Type") ?? "", variables)
        };

        return node;
    }

    private static LevelNode ImageNode(XElement element, int x, int y, string factor, IReadOnlyDictionary<string, string> variables, AffineContext parentContext)
    {
        var node = BasicNode(element, LevelNodeKind.Image, x, y, factor, variables);
        node.ClassName = ResolveString((string?)element.Attribute("ClassName") ?? node.Name, variables);
        node.ImagePath = Vector2Catalog.ResolveImagePath(node.ClassName);
        node.NativeX = IntAttr(element, "NativeX", 0, variables);
        node.NativeY = IntAttr(element, "NativeY", 0, variables);
        node.VisualType = IntAttr(element, "Type", 3, variables);
        node.VisualDepth = IntAttr(element, "Depth", 0, variables);
        ApplyImageTransform(element, node, parentContext, variables);
        return node;
    }

    private static LevelNode TrapezoidNode(XElement element, int x, int y, string factor, IReadOnlyDictionary<string, string> variables)
    {
        var node = BasicNode(element, LevelNodeKind.Trapezoid, x, y, factor, variables);
        node.Variant = ((string?)element.Attribute("Type") == "2") ? "Type2" : "Slope";
        return node;
    }

    private static LevelNode LibraryCommentVisualNode(XElement element, int x, int y, string factor, IReadOnlyDictionary<string, string> variables)
    {
        var node = BasicNode(element, LevelNodeKind.Area, x, y, factor, variables);
        node.Name = ResolveString((string?)element.Attribute("Text") ?? (string?)element.Attribute("Name") ?? "Area", variables);
        node.Template = "CommentPreview";
        node.Tag = "Area";
        node.Variant = "Animation";
        return node;
    }

    private static bool IsPhantomLibrary(string libraryPath)
        => Path.GetFileName(libraryPath).Equals("phantoms.xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsAreaLikePhantom(string name)
    {
        var normalized = new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        return normalized.Contains("wallrun", StringComparison.Ordinal) ||
               normalized.Contains("walljump", StringComparison.Ordinal) ||
               normalized.Contains("activator", StringComparison.Ordinal) ||
               normalized.Contains("trigger", StringComparison.Ordinal) ||
               normalized.Contains("runfromfail", StringComparison.Ordinal) ||
               normalized.Contains("runfast", StringComparison.Ordinal);
    }

    private static bool TryApplyStuntIconPreview(LevelNode reference, string lookupName, string phantomLibraryPath)
    {
        if (IsAreaLikePhantom(lookupName)) return false;
        if (!TryResolveStuntIcon(lookupName, out var className, out var icon)) return false;

        var (offsetX, offsetY, width, height) = RuntimeStuntIconGeometry(phantomLibraryPath);

        var piece = new LevelNode
        {
            Name = lookupName,
            Kind = LevelNodeKind.Image,
            X = reference.X + offsetX,
            Y = reference.Y + offsetY,
            Width = width,
            Height = height,
            Factor = "0",
            ClassName = className,
            ImagePath = icon,
            SortingLayer = "Default",
            Tag = "Image",
            Template = "PhantomStuntIcon"
        };
        piece.MarkPreviewOnly(true);
        reference.Children.Add(piece);
        reference.Width = piece.Width;
        reference.Height = piece.Height;
        reference.VisualOffsetX = 0;
        reference.VisualOffsetY = 0;
        return true;
    }

    private static (int OffsetX, int OffsetY, int Width, int Height) RuntimeStuntIconGeometry(string phantomLibraryPath)
    {
        const int fallbackSize = 96;
        var triggersPath = Vector2Catalog.ResolveLibraryPath("triggers.xml", phantomLibraryPath);
        if (string.IsNullOrWhiteSpace(triggersPath)) return (0, 0, fallbackSize, fallbackSize);

        var stuntObject = FindLibraryObject(triggersPath, "Stunt");
        var runtimeImage = stuntObject?.Descendants("Image").FirstOrDefault();
        if (runtimeImage is null) return (0, 0, fallbackSize, fallbackSize);

        var offsetX = IntAttr(runtimeImage, "X");
        var offsetY = IntAttr(runtimeImage, "Y");
        var width = IntAttr(runtimeImage, "Width", fallbackSize);
        var height = IntAttr(runtimeImage, "Height", fallbackSize);
        return (offsetX, offsetY, Math.Max(1, width), Math.Max(1, height));
    }

    private static IEnumerable<string> StuntIconCandidates(string normalized)
    {
        yield return $"stunts_run.track_trick_{normalized}";
        yield return $"stunts_run_low.track_trick_{normalized}";
        var compact = normalized.Replace("_", "", StringComparison.Ordinal);
        if (!compact.Equals(normalized, StringComparison.Ordinal))
        {
            yield return $"stunts_run.track_trick_{compact}";
            yield return $"stunts_run_low.track_trick_{compact}";
        }
    }

    private static string NormalizeStuntName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var chars = text
            .Replace("-", "_", StringComparison.Ordinal)
            .SelectMany((ch, index) =>
            {
                if (char.IsUpper(ch) && index > 0) return new[] { '_', char.ToLowerInvariant(ch) };
                return new[] { char.ToLowerInvariant(ch) };
            })
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray();
        return new string(chars).Replace("__", "_", StringComparison.Ordinal).Trim('_');
    }

    private static void ApplyPhantomVisualFallback(LevelNode reference, string lookupName, string libraryPath)
    {
        if (!Path.GetFileName(libraryPath).Equals("phantoms.xml", StringComparison.OrdinalIgnoreCase)) return;
        if (IsAreaLikePhantom(lookupName))
        {
            var areaBounds = BoundsForFallback(reference);
            reference.ImagePath = "";
            reference.Width = Math.Max(100, (int)Math.Round(areaBounds.Width));
            reference.Height = Math.Max(100, (int)Math.Round(areaBounds.Height));
            reference.VisualOffsetX = (int)Math.Round(areaBounds.Left - reference.X);
            reference.VisualOffsetY = (int)Math.Round(areaBounds.Top - reference.Y);
            reference.Children.Clear();
            var area = new LevelNode
            {
                Name = lookupName,
                Kind = LevelNodeKind.Area,
                X = (int)Math.Round(areaBounds.Left),
                Y = (int)Math.Round(areaBounds.Top),
                Width = reference.Width,
                Height = reference.Height,
                Factor = "1",
                SortingLayer = "Debug",
                Tag = "Area",
                Template = "PhantomArea",
                Variant = "Animation"
            };
            area.MarkPreviewOnly(true);
            reference.Children.Add(area);
            return;
        }
        if (reference.Children.SelectMany(child => child.Flatten()).Any(child => child.Kind == LevelNodeKind.Image && !string.IsNullOrWhiteSpace(child.ImagePath))) return;

        var bounds = BoundsForFallback(reference);
        if (TryCreateStuntIconNode(lookupName, bounds, out var icon))
        {
            icon.MarkPreviewOnly(true);
            reference.Children.Clear();
            reference.Children.Add(icon);
            return;
        }

        foreach (var node in reference.Children.SelectMany(child => child.Flatten()))
        {
            if (node.Kind != LevelNodeKind.Area) continue;
            node.Name = string.IsNullOrWhiteSpace(node.Name) || node.Name.Equals("Comment", StringComparison.OrdinalIgnoreCase)
                ? lookupName
                : node.Name;
        }
    }

    private static Rect BoundsForFallback(LevelNode reference)
    {
        var nodes = reference.Children.SelectMany(child => child.Flatten()).Where(child => !child.IsHidden).ToList();
        if (nodes.Count == 0) return new Rect(reference.X, reference.Y, Math.Max(72, reference.Width), Math.Max(72, reference.Height));

        var bounds = nodes[0].VisualBounds;
        foreach (var node in nodes.Skip(1))
        {
            bounds.Union(node.VisualBounds);
        }
        return new Rect(
            bounds.Left + (bounds.Width / 2.0) - (Math.Max(72, bounds.Width) / 2.0),
            bounds.Top + (bounds.Height / 2.0) - (Math.Max(72, bounds.Height) / 2.0),
            Math.Max(72, bounds.Width),
            Math.Max(72, bounds.Height));
    }

    private static bool TryCreateStuntIconNode(string name, Rect bounds, out LevelNode icon)
    {
        icon = new LevelNode();
        if (!TryResolveStuntIcon(name, out var className, out var imagePath)) return false;

        var width = Math.Max(72, (int)Math.Round(bounds.Width));
        var height = Math.Max(72, (int)Math.Round(bounds.Height));
        if (TryLoadBitmap(imagePath, out var image) && image.PixelWidth > 0 && image.PixelHeight > 0)
        {
            var scale = Math.Min((double)width / image.PixelWidth, (double)height / image.PixelHeight);
            width = Math.Max(24, (int)Math.Round(image.PixelWidth * scale));
            height = Math.Max(24, (int)Math.Round(image.PixelHeight * scale));
        }

        icon = new LevelNode
        {
            Name = name,
            Kind = LevelNodeKind.Image,
            X = (int)Math.Round(bounds.Left + (bounds.Width - width) / 2.0),
            Y = (int)Math.Round(bounds.Top + (bounds.Height - height) / 2.0),
            Width = width,
            Height = height,
            ClassName = className,
            ImagePath = imagePath,
            SortingLayer = "Debug",
            Factor = "0",
            Tag = "Image",
            Template = "PhantomStuntIcon"
        };
        return true;
    }

    public static bool TryResolveStuntIcon(string name, out string className, out string imagePath)
    {
        className = "";
        imagePath = "";
        var token = StuntIconToken(name);
        if (string.IsNullOrWhiteSpace(token)) return false;

        var classNames = new[]
        {
            $"stunts_run.track_trick_{token}",
            $"stunts_run.track_trick_{token}_0",
            $"stunts_run_low.track_trick_{token}",
            $"stunts_run_low.track_trick_{token}_0"
        };
        foreach (var candidate in classNames)
        {
            var path = Vector2Catalog.ResolveImagePathQuiet(candidate);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsExactStuntIconPath(path, token)) continue;
            className = candidate;
            imagePath = path;
            return true;
        }
        return false;
    }

    private static bool IsExactStuntIconPath(string path, string token)
    {
        var stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        const string marker = "track_trick_";
        var markerIndex = stem.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) return false;

        var resolvedToken = stem[(markerIndex + marker.Length)..];
        if (resolvedToken.EndsWith("_0", StringComparison.Ordinal))
        {
            resolvedToken = resolvedToken[..^2];
        }

        return resolvedToken.Equals(token, StringComparison.OrdinalIgnoreCase);
    }

    private static string StuntIconToken(string assetName)
    {
        var normalized = new string(assetName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["airbomb"] = "airbomb",
            ["airspin"] = "airspin",
            ["backflip"] = "backflip",
            ["barjump"] = "barjump",
            ["barjumpsaltoless"] = "barjumpsaltoless",
            ["boomboomsh"] = "boomboomshhuh",
            ["boomboomshhuh"] = "boomboomshhuh",
            ["cheatgainer"] = "cheatgainer",
            ["coolswing"] = "coolswing",
            ["dashtofrontflip"] = "dashtofrontflip",
            ["divedown"] = "divedown",
            ["diveroll"] = "diveroll",
            ["doubleback"] = "doubleback",
            ["doublejumproll"] = "doublejumproll",
            ["doublekong"] = "doublekong",
            ["divingkong"] = "doublekong",
            ["divingkongfly"] = "doublekong",
            ["doublespinvault"] = "doublespinvault",
            ["flyingarrow"] = "flyingarrow",
            ["wallhop"] = "wallhop360",
            ["wallhop360"] = "wallhop360",
            ["wallcling"] = "360wallcling",
            ["wallclingspin"] = "360wallcling",
            ["wallcling360"] = "360wallcling",
            ["360wallcling"] = "360wallcling",
            ["wallspeedvault"] = "wallspeedvault",
            ["wallbackroll"] = "wallbackroll",
            ["vertvault"] = "vertvault",
            ["sideflip"] = "sideflip",
            ["sidebomb"] = "sidebomb",
            ["spin"] = "spin360",
            ["spin360"] = "spin360",
            ["spinbicycle"] = "spinbicycle",
            ["spinvault"] = "spinvault",
            ["spinningvault"] = "spinningvault",
            ["splitone"] = "splitone",
            ["swallow"] = "swallow",
            ["swallow400"] = "swallow",
            ["jumpwheel"] = "jumpwheel",
            ["jumpobstacle"] = "jumpobstacle",
            ["jumpdownroll"] = "jumpdownroll",
            ["jumpandroll"] = "jumpdownroll",
            ["jumpspinvault"] = "jumpspinvault",
            ["jumptumble"] = "jumptumble",
            ["obstaclefrontflip"] = "obstaclefrontflip",
            ["barrelvault"] = "barrelvault",
            ["dashvault"] = "dashvault",
            ["gatevault"] = "gatevault",
            ["handspring"] = "handspring",
            ["handspringtoroll"] = "handspringtoroll",
            ["longcircle"] = "longcircle",
            ["longjumptobarrel"] = "longjumptobarrel",
            ["monkeytobackflip"] = "monkeytobackflip",
            ["monkeytobomb"] = "monkeytobomb",
            ["monkeyvault"] = "monkeyvault",
            ["monkeyvaultfly"] = "monkeyvault",
            ["popvault"] = "monkeyvault",
            ["popvaultclose"] = "monkeyvault",
            ["railflipvault"] = "railflip",
            ["railflip"] = "railflip",
            ["reversevault"] = "reversevault",
            ["rocketvault"] = "rocketvault",
            ["rolltostraightlegsflip"] = "rolltostraightlegsflip",
            ["screwdriver"] = "screwdriver",
            ["slowspin"] = "slowspin",
            ["thiefvault"] = "thiefvault",
            ["triplehit"] = "triplehit",
            ["tripleswing"] = "tripleswing",
            ["tripletricktoswallow"] = "tripletricktoswallow",
            ["underbar"] = "underbar",
            ["webster"] = "webster",
            ["websterwithsalto"] = "websterwithsalto",
            ["frontfliptwolegs"] = "frontfliplegsup",
            ["frontfliplegsup"] = "frontfliplegsup",
            ["doublespinroll"] = "doublespintoroll",
            ["doublespintoroll"] = "doublespintoroll",
            ["kingkongjumpoff"] = "kingkongjump",
            ["kingkongjump"] = "kingkongjump",
            ["kingkongtobend"] = "kingkongtobend",
            ["speedvault"] = "dashvault",
            ["speedvaultfly"] = "dashvault",
            ["hurdlejump"] = "jumpobstacle",
            ["hurdlejumptofly"] = "jumpobstacle",
            ["shortjump"] = "jumpobstacle",
            ["shortjumptofly"] = "jumpobstacle"
        };

        return aliases.TryGetValue(normalized, out var alias) ? alias : normalized;
    }

    private static (double A, double B, double C, double D, double Tx, double Ty) ReadMatrix(XElement element)
    {
        var matrix = element.Element("Properties")?.Element("Static")?.Element("Matrix");
        return matrix is null
            ? (1, 0, 0, 1, 0, 0)
            : (DoubleAttr(matrix, "A", 1), DoubleAttr(matrix, "B", 0), DoubleAttr(matrix, "C", 0), DoubleAttr(matrix, "D", 1), DoubleAttr(matrix, "Tx", 0), DoubleAttr(matrix, "Ty", 0));
    }

    private static void ApplyImageTransform(XElement element, LevelNode node, AffineContext parentContext, IReadOnlyDictionary<string, string> variables)
    {
        var localX = IntAttr(element, "X", 0, variables);
        var localY = IntAttr(element, "Y", 0, variables);
        var matrix = ReadMatrix(element);
        if (element.Element("Properties")?.Element("Static")?.Element("Matrix") is null)
        {
            matrix = (node.Width, 0, 0, node.Height, 0, 0);
        }
        var localAnchorX = localX + matrix.Tx;
        var localAnchorY = localY + matrix.Ty;
        var anchor = parentContext.Point(localAnchorX, localAnchorY);
        var basisX = parentContext.Vector(matrix.A, matrix.B);
        var basisY = parentContext.Vector(matrix.C, matrix.D);
        var corners = new[]
        {
            anchor,
            (anchor.X + basisX.X, anchor.Y + basisX.Y),
            (anchor.X + basisX.X + basisY.X, anchor.Y + basisX.Y + basisY.Y),
            (anchor.X + basisY.X, anchor.Y + basisY.Y)
        };

        node.X = (int)Math.Round(anchor.X);
        node.Y = (int)Math.Round(anchor.Y);
        node.Width = Math.Max(1, (int)Math.Round(Math.Sqrt((basisX.X * basisX.X) + (basisX.Y * basisX.Y))));
        node.Height = Math.Max(1, (int)Math.Round(Math.Sqrt((basisY.X * basisY.X) + (basisY.Y * basisY.Y))));
        node.Rotation = Math.Round(Math.Atan2(-basisX.Y, basisX.X) * 180.0 / Math.PI, 3);
        node.HasAffineBasis = true;
        node.BasisXX = basisX.X;
        node.BasisXY = basisX.Y;
        node.BasisYX = basisY.X;
        node.BasisYY = basisY.Y;
    }

    private static void ApplyMatrix(XElement element, LevelNode node, bool resizeFromBasis)
    {
        var matrix = element.Element("Properties")?.Element("Static")?.Element("Matrix");
        if (matrix is null) return;

        var a = DoubleAttr(matrix, "A", 1);
        var b = DoubleAttr(matrix, "B", 0);
        var c = DoubleAttr(matrix, "C", 0);
        var d = DoubleAttr(matrix, "D", 1);
        var tx = DoubleAttr(matrix, "Tx", 0);
        var ty = DoubleAttr(matrix, "Ty", 0);

        node.X += (int)Math.Round(tx);
        node.Y += (int)Math.Round(ty);

        if (!resizeFromBasis)
        {
            if (Math.Abs(a - 1) > 0.001 || Math.Abs(b) > 0.001)
            {
                node.Rotation = Math.Round(Math.Atan2(-b, a) * 180.0 / Math.PI, 3);
            }
            return;
        }

        var width = Math.Sqrt((a * a) + (b * b));
        var height = Math.Sqrt((c * c) + (d * d));
        if (width > 0.01) node.Width = Math.Max(1, (int)Math.Round(width));
        if (height > 0.01) node.Height = Math.Max(1, (int)Math.Round(height));
        node.Rotation = Math.Round(Math.Atan2(-b, a) * 180.0 / Math.PI, 3);
    }

    private static void ApplyPreviewBounds(LevelNode reference)
    {
        if (reference.Children.Count == 0) return;

        var boundsNodes = reference.Children
            .SelectMany(child => child.Flatten())
            .Where(child => child.Kind == LevelNodeKind.Image && !string.IsNullOrWhiteSpace(child.ImagePath))
            .ToList();
        if (boundsNodes.Count == 0)
        {
            boundsNodes = reference.Children.SelectMany(child => child.Flatten()).Where(child => !child.IsHidden).ToList();
        }
        if (boundsNodes.Count == 0) return;

        var left = boundsNodes.Min(c => c.VisualBounds.Left);
        var top = boundsNodes.Min(c => c.VisualBounds.Top);
        var right = boundsNodes.Max(c => c.VisualBounds.Right);
        var bottom = boundsNodes.Max(c => c.VisualBounds.Bottom);
        reference.Width = Math.Max(24, (int)Math.Round(right - left));
        reference.Height = Math.Max(24, (int)Math.Round(bottom - top));
        reference.VisualOffsetX = (int)Math.Round(left - reference.X);
        reference.VisualOffsetY = (int)Math.Round(top - reference.Y);
    }

    private static void ApplyRasterizedPreview(LevelNode reference, string lookupName, string libraryPath)
    {
        var imagePieces = reference.Children
            .SelectMany(child => child.Flatten())
            .Where(child => child.Kind == LevelNodeKind.Image && !string.IsNullOrWhiteSpace(child.ImagePath))
            .OrderBy(DrawOrder)
            .ToList();
        if (imagePieces.Count == 0) return;

        var bounds = PrimaryPreviewBounds(reference);
        if (bounds.Width < 1 || bounds.Height < 1) return;
        var previewPath = RasterizedPreviewPath(lookupName, libraryPath, imagePieces, bounds);
        if (string.IsNullOrWhiteSpace(previewPath)) return;

        reference.ImagePath = previewPath;
        reference.Width = Math.Max(24, (int)Math.Round(bounds.Width));
        reference.Height = Math.Max(24, (int)Math.Round(bounds.Height));
        reference.VisualOffsetX = (int)Math.Round(bounds.Left - reference.X);
        reference.VisualOffsetY = (int)Math.Round(bounds.Top - reference.Y);
    }

    private static string RasterizedPreviewPath(string lookupName, string libraryPath, IReadOnlyList<LevelNode> imagePieces, Rect bounds)
    {
        try
        {
            var safeName = System.Text.RegularExpressions.Regex.Replace(lookupName, @"[^A-Za-z0-9_-]+", "_");
            var hash = StableHash($"{PreviewCacheVersion}:{libraryPath}:{lookupName}:{imagePieces.Count}:{bounds.Left:0.###}:{bounds.Top:0.###}:{bounds.Width:0.###}:{bounds.Height:0.###}");
            var root = Path.Combine(Path.GetTempPath(), "Vector2LevelEditorLibraryPreviewsWpf");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"{safeName[..Math.Min(48, safeName.Length)]}_{hash}.png");
            if (File.Exists(path)) return path;

            const double maxSide = 1200;
            var scale = Math.Min(1.0, maxSide / Math.Max(bounds.Width, bounds.Height));
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                foreach (var piece in imagePieces)
                {
                    if (!TryLoadBitmap(piece.ImagePath, out var image)) continue;
                    if (piece.HasAffineBasis)
                    {
                        var matrix = new Matrix(
                            piece.BasisXX * scale / image.PixelWidth,
                            piece.BasisXY * scale / image.PixelWidth,
                            piece.BasisYX * scale / image.PixelHeight,
                            piece.BasisYY * scale / image.PixelHeight,
                            (piece.X - bounds.Left) * scale,
                            (piece.Y - bounds.Top) * scale);
                        dc.PushTransform(new MatrixTransform(matrix));
                        dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                        dc.Pop();
                    }
                    else
                    {
                        var rect = new Rect(
                            (piece.X - bounds.Left) * scale,
                            (piece.Y - bounds.Top) * scale,
                            piece.Width * scale,
                            piece.Height * scale);
                        dc.PushTransform(new RotateTransform(piece.Rotation, rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0));
                        dc.DrawImage(image, rect);
                        dc.Pop();
                    }
                }
            }

            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not bake runtime graph preview for '{lookupName}': {ex.Message}");
            return "";
        }
    }

    private static bool TryLoadBitmap(string path, out BitmapImage image)
    {
        image = new BitmapImage();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Rect PrimaryPreviewBounds(LevelNode node)
    {
        var boundsNodes = node.Flatten()
            .Where(child => child.Kind == LevelNodeKind.Image && !string.IsNullOrWhiteSpace(child.ImagePath))
            .ToList();
        if (boundsNodes.Count == 0)
        {
            boundsNodes = node.Flatten().Where(child => !child.IsHidden && child.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor)).ToList();
        }
        if (boundsNodes.Count == 0) return node.VisualBounds;

        var bounds = boundsNodes[0].VisualBounds;
        foreach (var child in boundsNodes.Skip(1))
        {
            bounds.Union(child.VisualBounds);
        }
        return bounds;
    }

    public static Rect OwnerPreviewBounds(LevelNode node)
        => !string.IsNullOrWhiteSpace(node.ImagePath) && node.RendersAsRuntimeGraph
            ? new Rect(node.X + node.VisualOffsetX, node.Y + node.VisualOffsetY, node.Width, node.Height)
            : PrimaryPreviewBounds(node);

    private static int DrawOrder(LevelNode node)
        => node.Kind switch
        {
            LevelNodeKind.Image => 0,
            LevelNodeKind.Platform => 10,
            LevelNodeKind.Trapezoid => 11,
            LevelNodeKind.Trigger or LevelNodeKind.Area => 20,
            _ => 30
        };

    private static string StableHash(string text)
    {
        unchecked
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;
            var hash = offset;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= prime;
            }
            return hash.ToString("x");
        }
    }

    private static int DefaultWidth(LevelNodeKind kind) => kind switch
    {
        LevelNodeKind.GateIn or LevelNodeKind.GateOut => 72,
        LevelNodeKind.Trigger => 180,
        LevelNodeKind.Area => 240,
        LevelNodeKind.Platform => 300,
        _ => 100
    };

    private static int DefaultHeight(LevelNodeKind kind) => kind switch
    {
        LevelNodeKind.GateIn or LevelNodeKind.GateOut => 72,
        LevelNodeKind.Trigger => 100,
        LevelNodeKind.Area => 120,
        LevelNodeKind.Platform => 120,
        _ => 100
    };

    private static Dictionary<string, string> CollectDefaultVariables(XElement objectXml)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in objectXml.Descendants("ContentVariable").Descendants().Where(e => e.Name.LocalName is "Variable" or "Constant"))
        {
            var name = (string?)variable.Attribute("Name") ?? "";
            var value = (string?)variable.Attribute("Default") ?? (string?)variable.Attribute("Value") ?? "";
            if (!string.IsNullOrWhiteSpace(name))
            {
                variables[name] = ResolveString(value, variables);
            }
        }
        return variables;
    }

    private static Dictionary<string, string> CollectOverrideVariables(XElement element, IReadOnlyDictionary<string, string> variables)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in element.Descendants("OverrideVariable").Descendants("Variable"))
        {
            var name = (string?)variable.Attribute("Name") ?? "";
            var value = (string?)variable.Attribute("Value") ?? "";
            if (!string.IsNullOrWhiteSpace(name))
            {
                overrides[name] = ResolveString(value, variables);
            }
        }
        return overrides;
    }

    private static string ResolveString(string raw, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        if (raw.StartsWith("~", StringComparison.Ordinal) && variables.TryGetValue(raw[1..], out var wholeValue))
        {
            return wholeValue;
        }
        return System.Text.RegularExpressions.Regex.Replace(raw, @"~[A-Za-z0-9_]+", match =>
        {
            var key = match.Value[1..];
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private static int IntAttr(XElement element, string name, int fallback = 0, IReadOnlyDictionary<string, string>? variables = null)
    {
        var raw = ((string?)element.Attribute(name)) ?? "";
        if (variables is not null) raw = ResolveString(raw, variables);
        raw = raw.Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return (int)Math.Round(number);
        }
        return fallback;
    }

    private static double DoubleAttr(XElement element, string name, double fallback = 0)
    {
        var raw = ((string?)element.Attribute(name))?.Replace(',', '.');
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
