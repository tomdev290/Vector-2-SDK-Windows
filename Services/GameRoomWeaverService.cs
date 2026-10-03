using System.IO;
using System.Xml.Linq;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// WPF-safe adaptation of Vector 2's Element.Parse selection rules. The game skips
/// Phantom=1 nodes and disabled/unchosen static selections before constructing runners;
/// Room Weaver does the same before handing the room to the editor scene parser.
/// </summary>
public sealed class GameRoomWeaverService
{
    private readonly XmlSceneParser _sceneParser = new();
    private readonly Dictionary<string, (long Length, DateTime Modified, XDocument Document)> _cache = new(StringComparer.OrdinalIgnoreCase);

    private XDocument ReadSource(string path)
    {
        path = Path.GetFullPath(path);
        var file = new FileInfo(path);
        if (!_cache.TryGetValue(path, out var entry) || entry.Length != file.Length || entry.Modified != file.LastWriteTimeUtc)
        {
            entry = (file.Length, file.LastWriteTimeUtc, XDocument.Load(path, LoadOptions.PreserveWhitespace));
            if (_cache.Count >= 16) _cache.Clear();
            _cache[path] = entry;
        }
        return new XDocument(entry.Document);
    }

    public IReadOnlyDictionary<string, string[]> ChoiceOptions(string path)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var selection = Selection(ReadSource(path));
        void Visit(XElement parent, string? parentPath)
        {
            foreach (var choice in parent.Elements("Choice"))
            {
                var name = (string?)choice.Attribute("Name") ?? "";
                if (name.Length == 0) continue;
                var qualified = parentPath is null ? name : parentPath + "/" + name;
                result[qualified] = choice.Elements("Variant").Select(item => (string?)item.Attribute("Name"))
                    .OfType<string>().Where(value => value.Length > 0).ToArray();
                foreach (var variant in choice.Elements("Variant"))
                {
                    var variantName = (string?)variant.Attribute("Name") ?? "";
                    var nested = parentPath is null ? name + "." + variantName : parentPath + "." + variantName;
                    Visit(variant, nested);
                }
            }
        }
        if (selection is not null) Visit(selection, null);
        return result;
    }

    private static XElement? Selection(XDocument document) => document.Root?.Element("Track")?.Element("Properties")?.Element("Selection")
        ?? document.Root?.Element("Properties")?.Element("Selection");

    public LevelDocument Load(string path, IReadOnlyDictionary<string, string>? selectedVariants = null)
    {
        var document = ReadSource(path);
        var roomName = Path.GetFileNameWithoutExtension(path);
        if (selectedVariants is not null)
        {
            var options = ChoiceOptions(path);
            foreach (var pair in selectedVariants)
            {
                if (!options.TryGetValue(pair.Key, out var variants) || !variants.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unknown room variant {pair.Key}: {pair.Value}.");
            }
        }
        var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["__RoomName"] = roomName };
        var catalogue = Selection(document);
        if (catalogue is not null) ResolveChoices(catalogue, null, roomName, selectedVariants, choices);
        var content = document.Root?.Element("Track")?.Element("Content")
                      ?? document.Root?.Element("Content");
        if (content is not null) FilterChildren(content, choices);
        var selection = document.Root?.Element("Track")?.Element("Properties")?.Element("Selection")
            ?? document.Root?.Element("Properties")?.Element("Selection");
        foreach (var choice in selection?.Descendants("Choice").ToList() ?? [])
        {
            var key = QualifiedChoice(choice);
            if (choices.TryGetValue(key, out var selected))
                choice.Elements("Variant").Where(variant => !string.Equals((string?)variant.Attribute("Name"), selected, StringComparison.OrdinalIgnoreCase)).Remove();
        }

        DiagnosticsLog.Info($"Game Room Weaver imported '{Path.GetFileName(path)}' with {choices.Count} resolved selection choice(s).");
        return _sceneParser.LoadFromString(document.ToString(SaveOptions.DisableFormatting), Path.GetFileName(path), path);
    }

    private static string QualifiedChoice(XElement choice)
    {
        var ancestors = choice.Ancestors("Variant").Reverse().ToList();
        var name = (string?)choice.Attribute("Name") ?? "";
        if (ancestors.Count == 0) return name;
        var path = ((string?)ancestors[0].Parent?.Attribute("Name") ?? "") + "." + (string?)ancestors[0].Attribute("Name");
        foreach (var variant in ancestors.Skip(1)) path += "." + (string?)variant.Attribute("Name");
        return path + "/" + name;
    }

    private static void ResolveChoices(XElement parent, string? parentPath, string roomName,
        IReadOnlyDictionary<string, string>? requested, IDictionary<string, string> choices)
    {
        foreach (var choice in parent.Elements("Choice"))
        {
            var name = (string?)choice.Attribute("Name") ?? "";
            var qualified = parentPath is null ? name : parentPath + "/" + name;
            var prefixed = roomName + "_" + qualified;
            string? target = null;
            if (requested is not null)
                target = requested.GetValueOrDefault(prefixed) ?? requested.GetValueOrDefault(qualified) ?? requested.GetValueOrDefault(name);
            var variant = choice.Elements("Variant").FirstOrDefault(item => (string?)item.Attribute("Name") == target)
                ?? choice.Elements("Variant").FirstOrDefault();
            var variantName = (string?)variant?.Attribute("Name") ?? "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(variantName)) continue;
            if (parentPath is null) choices[name] = variantName;
            choices[qualified] = variantName;
            choices[prefixed] = variantName;
            var nested = parentPath is null ? name + "." + variantName : parentPath + "." + variantName;
            if (variant is not null) ResolveChoices(variant, nested, roomName, requested, choices);
        }
    }

    private static void FilterChildren(XElement parent, IReadOnlyDictionary<string, string> choices)
    {
        foreach (var child in parent.Elements().ToList())
        {
            if (ShouldSkip(child, choices))
            {
                child.Remove();
                continue;
            }
            var content = child.Element("Content");
            if (content is not null) FilterChildren(content, choices);
        }
    }

    private static bool ShouldSkip(XElement element, IReadOnlyDictionary<string, string> choices)
    {
        if ((string?)element.Attribute("Phantom") == "1") return true;
        var statics = element.Element("Properties")?.Element("Static");
        var enabled = (string?)statics?.Element("Enable")?.Attribute("Value");
        if (enabled is not null && (enabled.Equals("0") || enabled.Equals("false", StringComparison.OrdinalIgnoreCase))) return true;

        var selections = statics?.Elements("Selection").ToList() ?? [];
        var hasRule = false;
        foreach (var selection in selections)
        {
            var choice = (string?)selection.Attribute("Choice") ?? "";
            var variant = (string?)selection.Attribute("Variant") ?? "";
            if (choice.Length == 0 || variant.Length == 0) continue;
            hasRule = true;
            var parent = ((string?)selection.Attribute("Parent") ?? "").Trim();
            var key = parent.Length == 0 ? choice : parent + "/" + choice;
            var prefixed = choices.GetValueOrDefault("__RoomName") + "_" + key;
            if ((choices.TryGetValue(key, out var chosen) && chosen == variant) ||
                (choices.TryGetValue(prefixed, out chosen) && chosen == variant)) return false;
        }
        return hasRule;
    }
}
