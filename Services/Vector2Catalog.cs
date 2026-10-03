using System.Collections.ObjectModel;
using System.IO;
using System.Xml.Linq;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Portable asset discovery for the Windows editor.
/// It intentionally avoids hardcoded user paths. Public builds should resolve
/// assets from the workspace, app folder, or environment overrides.
/// </summary>
public sealed class Vector2Catalog
{
    private static Dictionary<string, string>? _texturePathByClassName;
    private static readonly HashSet<string> MissingTextureWarnings = new(StringComparer.OrdinalIgnoreCase);
    public static string ProjectAssetsOverride { get; set; } = "";
    public static string CustomTexturesOverride { get; set; } = "";

    public static readonly string[] SortingLayers =
    [
        "BgFurther", "BgVeryVeryFar", "BgVeryFar", "BgFar", "BgMiddle", "BgClose", "BgVeryClose",
        "Wall", "CAperture", "CApertureAdd", "CPanels", "CPanelsAdd", "CDecals", "CDecalsAdd",
        "CQuestDecals", "CQuestDecalsAdd", "HoloWarningSigns", "UnderFloorPanels", "CutScene",
        "Swarm", "Shadows", "StuntsLinearDodge", "Stunts", "Black", "BlackAdd", "TrapsColor",
        "TrapsBlack", "LaserLow1", "LaserLow2", "LaserMed", "LaserHigh", "LaserHigh2",
        "TrapsShadows", "Sequences", "Lights", "LightsAdd", "Model", "Items", "Particles",
        "Fg", "FgAdd", "Collision", "0", "Default", "Debug"
    ];

    public static readonly string[] Tags =
    [
        "Image", "Object", "ObjectReference", "Platform", "Trapezoid", "Trigger", "Area",
        "Spawn", "In", "Out", "Camera", "Bonus", "Item", "Model", "Animation", "Particle",
        "Dynamic", "Swarm", "Waypoint", "Untagged"
    ];

    public ObservableCollection<TextureAsset> Assets { get; } = [];

    public void Refresh()
    {
        ApplySnapshot(RefreshSnapshot());
    }

    public IReadOnlyList<TextureAsset> RefreshSnapshot()
    {
        _texturePathByClassName = null;
        MissingTextureWarnings.Clear();
        DiagnosticsLog.Info($"Refreshing asset catalog from: {AppContext.BaseDirectory}");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = new List<TextureAsset>();
        foreach (var asset in DiscoverAssets())
        {
            var key = $"{asset.Kind}|{asset.Group}|{asset.Category}|{asset.Name}|{asset.ClassName}|{asset.FilePath}|{asset.SourcePath}|{asset.InstanceKey}";
            if (seen.Add(key))
            {
                snapshot.Add(asset);
            }
        }
        DiagnosticsLog.Info($"Asset catalog refreshed: {snapshot.Count} assets, {BuildTextureIndex().Count} texture aliases.");
        return snapshot;
    }

    public void ApplySnapshot(IEnumerable<TextureAsset> snapshot)
    {
        Assets.Clear();
        foreach (var asset in snapshot)
        {
            Assets.Add(asset);
        }
    }

    private static IEnumerable<TextureAsset> DiscoverAssets()
    {
        if (Directory.Exists(CustomTexturesOverride))
        {
            DiagnosticsLog.Info($"Scanning game custom textures: {CustomTexturesOverride}");
            foreach (var file in SafeEnumerateFiles(CustomTexturesOverride, "*.*", SearchOption.TopDirectoryOnly, "custom textures")
                         .Where(IsProjectTextureFile))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                yield return new TextureAsset("Custom Textures", TextureAssetKind.Texture, "custom_textures", name,
                    name, file, TextureDefaultLayer(name), InstanceKey: file);
            }
        }

        foreach (var assetRoot in DistinctRoots(ProjectAssetRoots()))
        {
            var textureRoot = Path.Combine(assetRoot, "TextureBank");
            if (Directory.Exists(textureRoot))
            {
                DiagnosticsLog.Info($"Scanning Vector 2 textures: {textureRoot}");
                foreach (var file in SafeEnumerateFiles(textureRoot, "*.*", SearchOption.AllDirectories, "Vector 2 textures")
                             .Where(IsProjectTextureFile))
                {
                    var category = Path.GetFileName(Path.GetDirectoryName(file)) ?? "textures";
                    var name = Path.GetFileNameWithoutExtension(file);
                    yield return new TextureAsset("Vector 2 Textures", TextureAssetKind.Texture, category, name, TextureClassNameFromFileStem(name), file, TextureDefaultLayer(name), InstanceKey: file);
                }
            }

            var libraryRoot = Path.Combine(assetRoot, "Libraries");
            if (Directory.Exists(libraryRoot))
            {
                DiagnosticsLog.Info($"Scanning Vector 2 libraries: {libraryRoot}");
                foreach (var file in SafeEnumerateFiles(libraryRoot, "*.xml", SearchOption.AllDirectories, "Vector 2 libraries"))
                {
                    foreach (var entry in ReadLibraryObjects(file))
                    {
                        yield return entry;
                    }
                }
            }

            var prefabRoot = Path.Combine(assetRoot, "EditorPrefabBank");
            if (Directory.Exists(prefabRoot))
            {
                DiagnosticsLog.Info($"Scanning Vector 2 editor prefabs: {prefabRoot}");
                foreach (var file in SafeEnumerateFiles(prefabRoot, "*.prefab", SearchOption.AllDirectories, "Vector 2 editor prefabs"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var category = PrefabCategory(name);
                    var hasRunner = UnityPrefabObjectBuilder.TryGetRunnerInfo(file, out var runnerName, out var runnerIsArea);
                    var preview = hasRunner
                        ? Vector2LibraryObjectBuilder.TryResolveStuntIcon(runnerName, out _, out var stuntIconPath)
                            ? stuntIconPath
                            : ""
                        : ResolveImagePath(UnityPrefabObjectBuilder.FirstVisualClass(file));
                    var asset = new TextureAsset("Editor Prefabs", TextureAssetKind.Prefab, category, name, name, preview, "Default", file, file);
                    if (string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
                    {
                        asset = asset with
                        {
                            FilePath = hasRunner
                                ? Vector2LibraryObjectBuilder.BuildRunnerPreviewImage(asset, runnerName, runnerIsArea)
                                : Vector2LibraryObjectBuilder.BuildPlaceholderPreviewImage(asset)
                        };
                    }
                    yield return asset;
                }
            }
        }
    }

    public static string ResolveImagePath(string className)
        => ResolveImagePath(className, warnWhenMissing: true);

    public static string ResolveImagePathQuiet(string className)
        => ResolveImagePath(className, warnWhenMissing: false);

    private static string ResolveImagePath(string className, bool warnWhenMissing)
    {
        if (string.IsNullOrWhiteSpace(className)) return "";
        _texturePathByClassName ??= BuildTextureIndex();
        var keys = TextureAliasKeys(className).ToArray();
        foreach (var key in keys)
        {
            if (_texturePathByClassName.TryGetValue(key, out var path)) return path;
        }

        var lookupKey = keys.FirstOrDefault() ?? NormalizeClassName(className);
        var suffixMatch = _texturePathByClassName.FirstOrDefault(pair =>
            pair.Key.EndsWith("." + lookupKey, StringComparison.OrdinalIgnoreCase) ||
            pair.Key.EndsWith("_" + lookupKey, StringComparison.OrdinalIgnoreCase) ||
            lookupKey.EndsWith("." + pair.Key, StringComparison.OrdinalIgnoreCase) ||
            lookupKey.EndsWith("_" + pair.Key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(suffixMatch.Value)) return suffixMatch.Value;

        if (warnWhenMissing && MissingTextureWarnings.Add(lookupKey))
        {
            DiagnosticsLog.Warn($"Texture not resolved for ClassName='{className}'. Import will show a box until the texture alias is added.");
        }
        return "";
    }

    public static string ResolveSpriteClassName(string guid, string fileId)
    {
        if (string.IsNullOrWhiteSpace(guid)) return "";

        foreach (var assetRoot in DistinctRoots(ProjectAssetRoots()))
        {
            var mapPath = Path.Combine(assetRoot, "PrefabSpriteGuidMap.tsv");
            if (!File.Exists(mapPath)) continue;

            try
            {
                var exactKey = string.IsNullOrWhiteSpace(fileId) ? guid : $"{guid}:{fileId}";
                string fallback = "";
                foreach (var line in File.ReadLines(mapPath))
                {
                    var parts = line.Split('\t', StringSplitOptions.TrimEntries);
                    if (parts.Length < 2) continue;
                    if (parts[0].Equals(exactKey, StringComparison.OrdinalIgnoreCase)) return parts[1];
                    if (parts[0].Equals(guid, StringComparison.OrdinalIgnoreCase)) fallback = parts[1];
                }
                if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                DiagnosticsLog.Warn($"Could not read prefab sprite map '{mapPath}': {ex.Message}");
            }
        }

        return "";
    }

    public static string FindLibraryContainingObject(string objectName, string? relativeTo = null)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return "";

        if (!string.IsNullOrWhiteSpace(relativeTo))
        {
            var baseDirectory = Directory.Exists(relativeTo) ? relativeTo : Path.GetDirectoryName(relativeTo);
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                foreach (var file in SafeEnumerateFiles(baseDirectory, "*.xml", SearchOption.TopDirectoryOnly, "nearby library XML"))
                {
                    if (LibraryContainsObject(file, objectName)) return file;
                }
            }
        }

        foreach (var assetRoot in DistinctRoots(ProjectAssetRoots()))
        {
            var libraryRoot = Path.Combine(assetRoot, "Libraries");
            if (!Directory.Exists(libraryRoot)) continue;
            foreach (var file in SafeEnumerateFiles(libraryRoot, "*.xml", SearchOption.AllDirectories, "library lookup"))
            {
                if (LibraryContainsObject(file, objectName)) return file;
            }
        }

        return "";
    }

    public static string ResolveLibraryPath(string filename, string? relativeTo = null)
    {
        if (string.IsNullOrWhiteSpace(filename)) return "";

        if (Path.IsPathRooted(filename) && File.Exists(filename)) return filename;

        if (!string.IsNullOrWhiteSpace(relativeTo))
        {
            var baseDirectory = Directory.Exists(relativeTo) ? relativeTo : Path.GetDirectoryName(relativeTo);
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                var besideImport = Path.Combine(baseDirectory, filename);
                if (File.Exists(besideImport)) return besideImport;
            }
        }

        foreach (var assetRoot in DistinctRoots(ProjectAssetRoots()))
        {
            var direct = Path.Combine(assetRoot, "Libraries", filename);
            if (File.Exists(direct)) return direct;

            var deep = Directory.Exists(Path.Combine(assetRoot, "Libraries"))
                ? SafeEnumerateFiles(Path.Combine(assetRoot, "Libraries"), filename, SearchOption.AllDirectories, "library path lookup").FirstOrDefault()
                : null;
            if (!string.IsNullOrWhiteSpace(deep)) return deep;
        }

        DiagnosticsLog.Warn($"Library XML not resolved: '{filename}'. Set VECTOR2_PROJECT_ASSETS_ROOT or bundle ProjectAssets.");
        return "";
    }

    public static string ResolvePrefabPath(string filename, string? relativeTo = null)
    {
        if (string.IsNullOrWhiteSpace(filename)) return "";
        if (Path.IsPathRooted(filename) && File.Exists(filename)) return filename;

        if (!string.IsNullOrWhiteSpace(relativeTo))
        {
            var baseDirectory = Directory.Exists(relativeTo) ? relativeTo : Path.GetDirectoryName(relativeTo);
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                var besideImport = Path.Combine(baseDirectory, filename);
                if (File.Exists(besideImport)) return besideImport;
            }
        }

        foreach (var assetRoot in DistinctRoots(ProjectAssetRoots()))
        {
            var direct = Path.Combine(assetRoot, "EditorPrefabBank", filename);
            if (File.Exists(direct)) return direct;
            var root = Path.Combine(assetRoot, "EditorPrefabBank");
            var deep = Directory.Exists(root)
                ? SafeEnumerateFiles(root, filename, SearchOption.AllDirectories, "prefab path lookup").FirstOrDefault()
                : null;
            if (!string.IsNullOrWhiteSpace(deep)) return deep;
        }

        DiagnosticsLog.Warn($"Editor prefab not resolved: '{filename}'.");
        return "";
    }

    private static Dictionary<string, string> BuildTextureIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(CustomTexturesOverride))
        {
            foreach (var file in SafeEnumerateFiles(CustomTexturesOverride, "*.*", SearchOption.TopDirectoryOnly, "custom texture index").Where(IsProjectTextureFile))
            {
                AddTextureAliases(index, Path.GetFileNameWithoutExtension(file), file);
            }
        }
        foreach (var assetRoot in DistinctRoots(ProjectAssetRoots()))
        {
            var textureRoot = Path.Combine(assetRoot, "TextureBank");
            if (!Directory.Exists(textureRoot)) continue;
            foreach (var file in SafeEnumerateFiles(textureRoot, "*.*", SearchOption.AllDirectories, "texture index").Where(IsProjectTextureFile))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                AddTextureAliases(index, name, file);
                var doubleUnderscore = name.Replace("__", ".");
                AddTextureAliases(index, doubleUnderscore, file);
                var afterPrefix = name.Contains("__") ? name[(name.IndexOf("__", StringComparison.Ordinal) + 2)..] : name;
                AddTextureAliases(index, afterPrefix, file);
            }
        }
        if (index.Count == 0)
        {
            DiagnosticsLog.Warn("No Vector 2 textures indexed. Check bundled ProjectAssets or VECTOR2_PROJECT_ASSETS_ROOT.");
        }
        return index;
    }

    private static string NormalizeClassName(string value)
    {
        var name = value.Trim().Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        // Class names are allowed to contain dots. Path.GetFileNameWithoutExtension
        // turns "black_low__black.manipulator_alpha_1" into "black_low__black",
        // which nukes most Windows library previews. Only strip real image suffixes.
        var extension = Path.GetExtension(name);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^extension.Length];
        }

        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+\(\d+\)$", "");

        return name
            .Replace("__", ".")
            .Replace("_low", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "_")
            .Trim()
            .ToLowerInvariant();
    }

    private static string TextureClassNameFromFileStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return "";

        var className = stem.Trim();
        var separator = className.LastIndexOf("__", StringComparison.Ordinal);
        if (separator >= 0 && separator + 2 < className.Length)
        {
            className = className[(separator + 2)..];
        }

        return className.Replace("__", ".").Replace("-", "_").Trim();
    }

    private static string TextureDefaultLayer(string stem)
    {
        var className = TextureClassNameFromFileStem(stem).ToLowerInvariant();
        if (className.Contains("black")) return "Black";
        if (className.Contains("shadow")) return "Shadows";
        if (className.Contains("wall") || className.Contains("zone")) return "Wall";
        return "Default";
    }

    private static void AddTextureAliases(Dictionary<string, string> index, string name, string file)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var alias in TextureAliasKeys(name))
        {
            index.TryAdd(alias, file);
        }
    }

    private static IEnumerable<string> TextureAliasKeys(string name)
    {
        var normalized = NormalizeClassName(name);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalized,
            normalized.Replace(".", "_"),
            normalized.Replace("_", ".")
        };

        var pieces = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length > 0) aliases.Add(pieces[^1]);
        if (pieces.Length > 1) aliases.Add(string.Join('.', pieces.Skip(1)));
        if (pieces.Length > 2 && pieces[0].Equals(pieces[1], StringComparison.OrdinalIgnoreCase)) aliases.Add(string.Join('.', pieces.Skip(1)));

        pieces = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length > 0) aliases.Add(pieces[^1]);
        if (pieces.Length > 1) aliases.Add(string.Join('_', pieces.Skip(1)));

        foreach (var alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
        {
            yield return alias;
        }
    }

    private static IEnumerable<TextureAsset> ReadLibraryObjects(string file)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            DiagnosticsLog.Warn($"Could not parse library XML: {file}");
            yield break;
        }

        var category = Path.GetFileNameWithoutExtension(file);
        var index = 0;
        foreach (var element in TopLevelLibraryObjects(document).Where(e => e.Attribute("Name") is not null || e.Attribute("Class") is not null))
        {
            index++;
            var name = (string?)element.Attribute("Name") ?? (string?)element.Attribute("Class") ?? element.Name.LocalName;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var preview = ResolveLibraryPreviewImage(element, file, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var instanceKey = $"{file}#{index}:{name}";
            var asset = new TextureAsset("Library", TextureAssetKind.LibraryObject, category, name, name, preview, "Default", file, instanceKey);
            var bakedPreview = Vector2LibraryObjectBuilder.BuildAssetPreviewImage(asset);
            var finalPreview = string.IsNullOrWhiteSpace(bakedPreview) ? preview : bakedPreview;
            if (string.IsNullOrWhiteSpace(finalPreview) || !File.Exists(finalPreview))
            {
                finalPreview = Vector2LibraryObjectBuilder.BuildPlaceholderPreviewImage(asset);
            }
            yield return asset with { FilePath = finalPreview };

            foreach (var variantName in LibraryVariantNames(element, name))
            {
                var variantKey = $"{file}#{index}:{name}:{variantName}";
                var variantAsset = new TextureAsset("Library", TextureAssetKind.LibraryObject, category, variantName, name, preview, "Default", file, variantKey);
                var variantPreview = Vector2LibraryObjectBuilder.BuildAssetPreviewImage(variantAsset);
                if (string.IsNullOrWhiteSpace(variantPreview) || !File.Exists(variantPreview))
                {
                    variantPreview = finalPreview;
                }
                yield return variantAsset with { FilePath = variantPreview };
            }
        }
    }

    private static IEnumerable<string> LibraryVariantNames(XElement topLevelObject, string topLevelName)
    {
        var enabledVariableNames = topLevelObject
            .Descendants("ContentVariable")
            .Descendants("Variable")
            .Select(variable => ((string?)variable.Attribute("Name") ?? "", (string?)variable.Attribute("Type") ?? ""))
            .Where(variable => variable.Item2.Equals("E_Bool", StringComparison.OrdinalIgnoreCase))
            .Select(variable => variable.Item1)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Equals(topLevelName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabledVariableNames.Count == 0) yield break;

        foreach (var nested in topLevelObject.Descendants("Object"))
        {
            var name = (string?)nested.Attribute("Name") ?? "";
            if (enabledVariableNames.Contains(name))
            {
                yield return name;
            }
        }
    }

    private static string ResolveLibraryPreviewImage(XElement element, string libraryPath, HashSet<string> visited)
    {
        var visitKey = $"{libraryPath}#{(string?)element.Attribute("Name") ?? (string?)element.Attribute("Class") ?? element.Name.LocalName}";
        if (!visited.Add(visitKey)) return "";

        var candidates = element
            .Descendants("Image")
            .Select(image => new
            {
                ClassName = (string?)image.Attribute("ClassName") ?? "",
                Area = IntAttr(image, "Width") * IntAttr(image, "Height")
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ClassName))
            .OrderByDescending(candidate => candidate.Area)
            .Select(candidate => candidate.ClassName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var path = ResolveImagePath(candidate, warnWhenMissing: false);
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }

        foreach (var reference in element.Descendants("ObjectReference"))
        {
            var filename = (string?)reference.Attribute("Filename") ?? "";
            var name = (string?)reference.Attribute("Name") ?? "";
            var referencedPath = ResolveLibraryPath(filename, libraryPath);
            if (string.IsNullOrWhiteSpace(referencedPath) || string.IsNullOrWhiteSpace(name)) continue;

            try
            {
                var document = XDocument.Load(referencedPath, LoadOptions.PreserveWhitespace);
                var referencedObject = TopLevelLibraryObjects(document)
                    .FirstOrDefault(e => string.Equals((string?)e.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals((string?)e.Attribute("Class"), name, StringComparison.OrdinalIgnoreCase));
                if (referencedObject is null) continue;
                var nestedPreview = ResolveLibraryPreviewImage(referencedObject, referencedPath, visited);
                if (!string.IsNullOrWhiteSpace(nestedPreview)) return nestedPreview;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or System.Xml.XmlException)
            {
                DiagnosticsLog.Warn($"Could not inspect nested library preview '{referencedPath}': {ex.Message}");
            }
        }

        return "";
    }

    private static int IntAttr(XElement element, string name)
        => int.TryParse((string?)element.Attribute(name), out var value) ? Math.Abs(value) : 1;

    private static bool LibraryContainsObject(string file, string objectName)
    {
        try
        {
            var document = XDocument.Load(file, LoadOptions.None);
            return TopLevelLibraryObjects(document).Any(e =>
                string.Equals((string?)e.Attribute("Name"), objectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)e.Attribute("Class"), objectName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
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

    private static IEnumerable<string> CandidateRoots(string environmentKey, string relativeProbe)
    {
        if (relativeProbe.EndsWith("ProjectAssets/Vector2", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ProjectAssetsOverride))
        {
            var resolvedOverride = ResolveAssetRoot(ProjectAssetsOverride);
            if (!string.IsNullOrWhiteSpace(resolvedOverride))
            {
                DiagnosticsLog.Info($"Asset root from settings: {resolvedOverride}");
                yield return resolvedOverride;
            }
            else
            {
                DiagnosticsLog.Warn($"Configured ProjectAssets root is missing: {ProjectAssetsOverride}");
            }
        }

        var env = Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var resolvedEnv = ResolveAssetRoot(env);
            if (!string.IsNullOrWhiteSpace(resolvedEnv))
            {
                DiagnosticsLog.Info($"Asset root from {environmentKey}: {resolvedEnv}");
                yield return resolvedEnv;
            }
            else
            {
                DiagnosticsLog.Warn($"{environmentKey} points to a missing folder: {env}");
            }
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "ProjectAssets", "Vector2");
        if (relativeProbe.EndsWith("ProjectAssets/Vector2", StringComparison.OrdinalIgnoreCase) && Directory.Exists(bundled))
        {
            DiagnosticsLog.Info($"Bundled asset root: {bundled}");
            yield return bundled;
        }

        foreach (var root in WorkspaceRoots())
        {
            var candidate = Path.Combine(root, relativeProbe);
            if (Directory.Exists(candidate))
            {
                DiagnosticsLog.Info($"Workspace asset root: {candidate}");
                yield return candidate;
            }

        }
    }

    private static IEnumerable<string> ProjectAssetRoots()
    {
        foreach (var root in CandidateRoots("VECTOR2_PROJECT_ASSETS_ROOT", "ProjectAssets/Vector2"))
        {
            yield return root;
        }

        foreach (var root in CandidateRoots("VECTOR2_ASSETS_ROOT", "ProjectAssets/Vector2"))
        {
            yield return root;
        }
    }

    private static string ResolveAssetRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return "";
        if (Directory.Exists(Path.Combine(path, "TextureBank")) || Directory.Exists(Path.Combine(path, "Libraries"))) return path;

        var vector2Child = Path.Combine(path, "Vector2");
        if (Directory.Exists(vector2Child) &&
            (Directory.Exists(Path.Combine(vector2Child, "TextureBank")) || Directory.Exists(Path.Combine(vector2Child, "Libraries"))))
        {
            return vector2Child;
        }

        var projectAssetsVector2 = Path.Combine(path, "ProjectAssets", "Vector2");
        if (Directory.Exists(projectAssetsVector2) &&
            (Directory.Exists(Path.Combine(projectAssetsVector2, "TextureBank")) || Directory.Exists(Path.Combine(projectAssetsVector2, "Libraries"))))
        {
            return projectAssetsVector2;
        }

        return "";
    }

    private static IEnumerable<string> DistinctRoots(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static IEnumerable<string> WorkspaceRoots()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++, current = current.Parent)
        {
            yield return current.FullName;
        }

        current = new DirectoryInfo(Environment.CurrentDirectory);
        for (var i = 0; i < 12 && current is not null; i++, current = current.Parent)
        {
            yield return current.FullName;
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }

    private static bool IsProjectTextureFile(string path)
    {
        if (!IsImageFile(path)) return false;
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/__MACOSX/", StringComparison.OrdinalIgnoreCase)) return false;
        if (normalized.Contains("/vector1_", StringComparison.OrdinalIgnoreCase)) return false;
        if (Path.GetFileName(path).StartsWith("vector1_", StringComparison.OrdinalIgnoreCase)) return false;
        if (Path.GetFileName(path).StartsWith("._", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static IReadOnlyList<string> SafeEnumerateFiles(string root, string pattern, SearchOption searchOption, string label)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, searchOption).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
        {
            DiagnosticsLog.Warn($"Skipping {label} scan at '{root}': {ex.Message}");
            return [];
        }
    }

    private static string PrefabCategory(string name)
    {
        var pieces = name.Split("__", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return pieces.Length > 1 ? pieces[0] : "prefabs";
    }
}
