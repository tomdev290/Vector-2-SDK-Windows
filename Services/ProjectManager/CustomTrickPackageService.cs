using System.Xml.Linq;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed record CustomTrickDraft(string Name, string VisualName, string Description, string AnimationPath,
    string Pivot, int EntryFrame, int SafeStart, int SafeEnd, string ExitAnimation, int ExitFrame,
    string Image, int Rarity, int Price);

public static class CustomTrickPackageService
{
    public static string SaveNew(string root, CustomTrickDraft draft)
    {
        var name = ProjectTemplateService.SafeId(draft.Name);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Enter a stable animation name.");
        if (char.IsDigit(name[0])) name = "Trick_" + name;
        var frames = GameTrickPreviewService.ValidateImportedFrames(draft.AnimationPath);
        if (draft.EntryFrame < 0 || draft.EntryFrame >= frames || draft.SafeStart < 0 ||
            draft.SafeEnd < draft.SafeStart || draft.SafeEnd >= frames ||
            draft.ExitFrame < 0 || draft.Rarity is < 1 or > 3 || draft.Price is < 0 or > 100000)
            throw new InvalidDataException("The timeline, rarity, or price is outside the valid range.");
        if (draft.Pivot is not ("DetectorH" or "DetectorV" or "COM"))
            throw new InvalidDataException("Choose a supported pivot.");
        var package = Path.Combine(root, name);
        if (Directory.Exists(package)) throw new IOException("That trick package already exists.");
        Directory.CreateDirectory(package);
        try
        {
            var bytesName = name + ".bytes";
            File.Copy(draft.AnimationPath, Path.Combine(package, bytesName));
            var rootNode = new XElement("CustomTrick",
                new XAttribute("Name", name), new XAttribute("FileName", bytesName),
                new XAttribute("PivotNode", draft.Pivot), new XAttribute("FirstFrame", 0),
                new XAttribute("EndFrame", frames - 1), new XAttribute("EntryFrame", draft.EntryFrame),
                new XAttribute("SafeStart", draft.SafeStart), new XAttribute("SafeEnd", draft.SafeEnd),
                new XAttribute("ExitAnimation", string.IsNullOrWhiteSpace(draft.ExitAnimation) ? "RunForward" : draft.ExitAnimation.Trim()),
                new XAttribute("ExitFrame", draft.ExitFrame),
                new XAttribute("VisualName", string.IsNullOrWhiteSpace(draft.VisualName) ? name : draft.VisualName.Trim()),
                new XAttribute("Description", string.IsNullOrWhiteSpace(draft.Description) ? "Custom trick: " + name : draft.Description.Trim()),
                new XAttribute("Image", draft.Image), new XAttribute("Rarity", draft.Rarity),
                new XAttribute("Price", draft.Price), new XAttribute("ShopEnabled", 1),
                new XAttribute("CardType", "Stunts"), new XAttribute("EffectID", "Stunts"),
                new XAttribute("Slot", "Stunts"), new XAttribute("Group", "CustomTricks"),
                new XAttribute("SetupMin", 0), new XAttribute("SetupMax", 99),
                new XAttribute("Weight", 1250), new XAttribute("WeightForUse", 1), new XAttribute("MaxLevel", 5));
            var upgrades = new[] { (2, 100), (3, 120), (5, 145), (7, 175), (9, 210) };
            for (var i = 0; i < upgrades.Length; i++)
                rootNode.Add(new XElement("Level", new XAttribute("Number", i + 1),
                    new XAttribute("Cards", upgrades[i].Item1), new XAttribute("Points", upgrades[i].Item2)));
            var manifest = Path.Combine(package, "trick.xml");
            new XDocument(rootNode).Save(manifest);
            return manifest;
        }
        catch
        {
            Directory.Delete(package, true);
            throw;
        }
    }

    public static string SaveOverride(string root, string target, string animationPath, int requiredFrames)
    {
        if (string.IsNullOrWhiteSpace(target) || Path.GetFileName(target) != target ||
            target.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Choose a valid animation target.");
        if (GameTrickPreviewService.ValidateImportedFrames(animationPath) < requiredFrames)
            throw new InvalidDataException($"The override needs at least {requiredFrames} frames.");
        var package = Path.Combine(root, "animation_overrides", target);
        Directory.CreateDirectory(package);
        var bytes = Path.Combine(package, "replacement.bytes");
        File.Copy(animationPath, bytes, true);
        var manifest = Path.Combine(package, "override.xml");
        new XDocument(new XElement("AnimationOverride", new XAttribute("Target", target),
            new XAttribute("FileName", "replacement.bytes"), new XAttribute("Enabled", 1))).Save(manifest);
        return manifest;
    }

    public static bool RestoreDefault(string root, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || Path.GetFileName(target) != target ||
            target.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Choose a valid animation target.");
        var overridesRoot = Path.GetFullPath(Path.Combine(root, "animation_overrides"));
        var package = Path.GetFullPath(Path.Combine(overridesRoot, target));
        if (!package.StartsWith(overridesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The override path is outside the animation package.");
        if (!Directory.Exists(package)) return false;
        Directory.Delete(package, true);
        return true;
    }
}
