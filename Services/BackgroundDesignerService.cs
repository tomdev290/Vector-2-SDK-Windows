using System.IO;
using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public sealed record CustomBackgroundCatalog(IReadOnlyList<CustomBackgroundDefinition> Definitions);

public static class BackgroundDesignerService
{
    public static readonly IReadOnlyList<string> Layers =
    [
        "BgVeryVeryFar", "BgVeryFar", "BgFar", "BgFurther", "BgMiddle", "BgClose", "BgVeryClose"
    ];

    public static string DefaultFactor(string layer) => layer switch
    {
        "BgVeryVeryFar" => "0.9",
        "BgVeryFar" => "0.85",
        "BgFar" or "BgFurther" => "0.8",
        "BgMiddle" => "0.75",
        "BgClose" => "0.7",
        "BgVeryClose" => "0.65",
        _ => "0.75"
    };

    public static CustomBackgroundCatalog LoadCatalog(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return new CustomBackgroundCatalog([]);
        var path = Path.Combine(folder, "custom_backgrounds.xml");
        if (!File.Exists(path)) return new CustomBackgroundCatalog([]);
        try
        {
            var root = XDocument.Load(path).Root;
            if (root is null) return new CustomBackgroundCatalog([]);
            var definitions = root.Elements("Background").Select(background => new CustomBackgroundDefinition
            {
                Name = (string?)background.Attribute("Name") ?? "",
                Pieces = background.Elements().Select(piece => piece.ToString()).ToList()
            }).Where(background => !string.IsNullOrWhiteSpace(background.Name)).ToList();
            return new CustomBackgroundCatalog(definitions);
        }
        catch
        {
            return new CustomBackgroundCatalog([]);
        }
    }

    public static IReadOnlyList<CustomBackgroundDefinition> Load(string folder)
        => LoadCatalog(folder).Definitions;

    public static void SaveCurrent(string folder, CustomBackgroundDefinition definition)
    {
        Directory.CreateDirectory(folder);
        var root = new XElement("CustomBackgrounds",
            new XAttribute("Version", "1"));
        var background = new XElement("Background",
            new XAttribute("Name", definition.Name));
        foreach (var piece in definition.Pieces)
        {
            try { background.Add(XElement.Parse(piece)); }
            catch { }
        }
        root.Add(background);

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var destination = Path.Combine(folder, "custom_backgrounds.xml");
        var temporary = Path.Combine(folder, $".custom_backgrounds.{Guid.NewGuid():N}.tmp");
        try
        {
            document.Save(temporary);
            if (File.Exists(destination))
                File.Replace(temporary, destination, null, true);
            else
                File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static bool IsBackground(LevelNode node)
        => node.Kind == LevelNodeKind.Image &&
           (node.Tag.Equals("Background", StringComparison.OrdinalIgnoreCase) ||
            node.SortingLayer.StartsWith("Bg", StringComparison.OrdinalIgnoreCase));
}
