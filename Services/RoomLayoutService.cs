using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class RoomLayoutService
{
    public static RoomLayoutSection? Match(string? choice)
    {
        var folded = (choice ?? "").Replace('_', ' ').Trim();
        return folded.ToLowerInvariant() switch
        {
            "start" or "start zone" => RoomLayoutSection.Start,
            "middle" or "middle zone" => RoomLayoutSection.Middle,
            "finish" or "finish zone" => RoomLayoutSection.Finish,
            "dynamic" or "dynamic zone" => RoomLayoutSection.Dynamic,
            _ => null
        };
    }

    public static IReadOnlyList<RoomLayoutOption> Options(LevelDocument document) =>
        document.Nodes.SelectMany(root => root.Flatten())
            .SelectMany(node => node.RoomLayoutRules.Select(rule => (Node: node, Rule: rule)))
            .GroupBy(item => (item.Rule.Section, item.Rule.Variant), new KeyComparer())
            .Select(group => new RoomLayoutOption(group.Key.Section, group.Key.Variant,
                group.First().Rule.Parent, group.Select(item => item.Node).DistinctBy(node => node.Id).ToList()))
            .OrderBy(option => option.Section).ThenBy(option => option.Variant, StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<LevelNode> SelectedNodes(LevelDocument document)
    {
        var ids = document.SelectedNodeIds.Count > 0 ? document.SelectedNodeIds :
            document.SelectedNode is { } selected ? new HashSet<Guid> { selected.Id } : [];
        return document.SceneNodes.Where(node => ids.Contains(node.Id) &&
            node.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor)).ToList();
    }

    public static RoomLayoutOption Create(LevelDocument document, RoomLayoutSection section, string rawName)
    {
        var variant = SafeVariant(rawName);
        if (variant.Length == 0) throw new InvalidDataException("Give this layout a name.");
        if (Options(document).Any(option => option.Section == section &&
            option.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"A {section} layout named {variant} already exists.");
        var nodes = SelectedNodes(document);
        if (nodes.Count == 0) throw new InvalidDataException("Select objects on the canvas first.");
        Assign(nodes, section, variant);
        return Options(document).Single(option => option.Section == section &&
            option.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase));
    }

    public static void Assign(IEnumerable<LevelNode> nodes, RoomLayoutSection section, string variant, string parent = "")
    {
        foreach (var node in nodes)
        {
            node.RoomLayoutRules.Clear();
            node.RoomLayoutRules.Add(new RoomLayoutRule(section, variant, parent));
        }
    }

    public static void MakeShared(IEnumerable<LevelNode> nodes)
    {
        foreach (var node in nodes) node.RoomLayoutRules.Clear();
    }

    public static void Delete(LevelDocument document, RoomLayoutOption option)
    {
        foreach (var node in option.Nodes)
        {
            foreach (var rule in node.RoomLayoutRules.Where(rule => rule.Section == option.Section &&
                rule.Variant.Equals(option.Variant, StringComparison.OrdinalIgnoreCase)).ToList())
                node.RoomLayoutRules.Remove(rule);
        }
    }

    public static void Rename(LevelDocument document, RoomLayoutOption option, string rawName)
    {
        var variant = SafeVariant(rawName);
        if (variant.Length == 0) throw new InvalidDataException("Give this layout a name.");
        if (Options(document).Any(other => other.Section == option.Section &&
            !other.Variant.Equals(option.Variant, StringComparison.OrdinalIgnoreCase) &&
            other.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"A {option.Section} layout named {variant} already exists.");
        var oldPath = new RoomLayoutRule(option.Section, option.Variant).Choice + "." + option.Variant;
        var newPath = new RoomLayoutRule(option.Section, variant).Choice + "." + variant;
        foreach (var node in document.Nodes.SelectMany(root => root.Flatten()))
        {
            for (var index = 0; index < node.RoomLayoutRules.Count; index++)
            {
                var rule = node.RoomLayoutRules[index];
                if (rule.Section == option.Section &&
                    rule.Variant.Equals(option.Variant, StringComparison.OrdinalIgnoreCase))
                    rule = rule with { Variant = variant };
                if (rule.Parent.Contains(oldPath, StringComparison.OrdinalIgnoreCase))
                    rule = rule with { Parent = rule.Parent.Replace(oldPath, newPath, StringComparison.OrdinalIgnoreCase) };
                node.RoomLayoutRules[index] = rule;
            }
        }
    }

    public static void SetParent(LevelDocument document, RoomLayoutOption option, RoomLayoutOption? parent)
    {
        if (parent is not null && parent.Section == option.Section &&
            parent.Variant.Equals(option.Variant, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A layout cannot be its own parent.");
        var options = Options(document);
        static string Key(RoomLayoutOption item) => item.Section + "|" + item.Variant;
        var byKey = options.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var parents = options.ToDictionary(Key, item =>
            item.Parent.Length == 0 ? "" : item.Parent.Split('/').Last().Replace(".", "|", StringComparison.Ordinal),
            StringComparer.OrdinalIgnoreCase);
        var parentKey = parent is null ? "" : Key(parent);
        if (parent is not null && !byKey.ContainsKey(parentKey))
            throw new InvalidDataException("The parent layout no longer exists.");
        foreach (var sibling in options.Where(item => item.Section == option.Section))
        {
            var key = Key(sibling);
            if (parent is null || key.Equals(Key(option), StringComparison.OrdinalIgnoreCase) ||
                parents[key].Length == 0)
                parents[key] = parentKey;
        }
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string PathFor(string key, HashSet<string> visiting)
        {
            if (resolved.TryGetValue(key, out var cached)) return cached;
            if (!visiting.Add(key)) throw new InvalidDataException("This link would create a cycle.");
            var current = byKey[key];
            var own = new RoomLayoutRule(current.Section, current.Variant).Choice + "." + current.Variant;
            var parentPath = parents[key].Length == 0 ? "" :
                byKey.ContainsKey(parents[key]) ? PathFor(parents[key], visiting) : "";
            visiting.Remove(key);
            return resolved[key] = parentPath.Length == 0 ? own : parentPath + "/" + own;
        }
        foreach (var key in byKey.Keys) PathFor(key, []);
        foreach (var node in document.Nodes.SelectMany(root => root.Flatten()))
        {
            for (var index = 0; index < node.RoomLayoutRules.Count; index++)
            {
                var rule = node.RoomLayoutRules[index];
                var key = rule.Section + "|" + rule.Variant;
                if (!byKey.ContainsKey(key)) continue;
                var path = resolved[key];
                var slash = path.LastIndexOf('/');
                node.RoomLayoutRules[index] = rule with { Parent = slash < 0 ? "" : path[..slash] };
            }
        }
    }

    public static IReadOnlyList<string> Validate(LevelDocument document)
    {
        var options = Options(document);
        var issues = new List<string>();
        foreach (var option in options)
        {
            if (option.Section == RoomLayoutSection.Start &&
                !option.Nodes.Any(node => node.Kind == LevelNodeKind.GateIn))
                issues.Add($"{option.DisplayName} needs an entrance gate.");
            if (option.Section == RoomLayoutSection.Finish &&
                !option.Nodes.Any(node => node.Kind == LevelNodeKind.GateOut))
                issues.Add($"{option.DisplayName} needs an exit gate.");
            if (option.Parent.Length > 0)
            {
                var last = option.Parent.Split('/').Last();
                if (!options.Any(candidate => new RoomLayoutRule(candidate.Section, candidate.Variant).Choice +
                    "." + candidate.Variant == last))
                    issues.Add($"{option.DisplayName} is linked to a missing layout.");
            }
        }
        return issues;
    }

    public static XElement? ExportCatalogue(LevelDocument document)
    {
        var options = Options(document);
        if (options.Count == 0) return null;
        var selection = new XElement("Selection");
        var roots = options.Where(option => option.Parent.Length == 0).ToList();
        foreach (var section in Enum.GetValues<RoomLayoutSection>())
        {
            var choice = BuildChoice(section, roots, options, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (choice is not null) selection.Add(choice);
        }
        return selection.HasElements ? selection : null;
    }

    private static XElement? BuildChoice(RoomLayoutSection section, IReadOnlyList<RoomLayoutOption> scoped,
        IReadOnlyList<RoomLayoutOption> all, HashSet<string> ancestors)
    {
        var selected = scoped.Where(option => option.Section == section).ToList();
        if (selected.Count == 0) return null;
        var choice = new XElement("Choice", new XAttribute("Name",
            new RoomLayoutRule(section, "").Choice));
        foreach (var option in selected)
        {
            var path = (option.Parent.Length > 0 ? option.Parent + "/" : "") +
                new RoomLayoutRule(option.Section, option.Variant).Choice + "." + option.Variant;
            var variant = new XElement("Variant", new XAttribute("Name", option.Variant));
            if (ancestors.Add(path))
            {
                var children = all.Where(child => child.Parent.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var childSection in Enum.GetValues<RoomLayoutSection>())
                {
                    var nested = BuildChoice(childSection, children, all, ancestors);
                    if (nested is not null) variant.Add(nested);
                }
                ancestors.Remove(path);
            }
            choice.Add(variant);
        }
        return choice;
    }

    private static string SafeVariant(string name) =>
        new string(name.Trim().Replace(' ', '_').Where(ch =>
            char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());

    private sealed class KeyComparer : IEqualityComparer<(RoomLayoutSection Section, string Variant)>
    {
        public bool Equals((RoomLayoutSection Section, string Variant) x, (RoomLayoutSection Section, string Variant) y) =>
            x.Section == y.Section && x.Variant.Equals(y.Variant, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((RoomLayoutSection Section, string Variant) key) =>
            HashCode.Combine(key.Section, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Variant));
    }
}
