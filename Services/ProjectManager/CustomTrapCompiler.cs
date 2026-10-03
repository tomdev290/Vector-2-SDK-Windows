using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Vector2LevelEditor.Services.ProjectManager;

public static class CustomTrapCompiler
{
    public static XElement DefaultManifest(string id, string name) => new("CustomTrap",
        new XAttribute("SchemaVersion", "1"), new XAttribute("ID", id), new XAttribute("Name", name),
        new XAttribute("Mechanism", "Contact hazard"), new XAttribute("Mount", "Floor"),
        new XAttribute("Artwork", ""), new XAttribute("Width", "180"), new XAttribute("Height", "180"),
        new XAttribute("CycleFrames", "0"), new XAttribute("Reach", "400"), new XAttribute("Lethal", "true"),
        new XAttribute("Impact", "Instant defeat"), new XAttribute("ArmorSlot", "Torso"), new XAttribute("DamageAmount", "1"),
        new XAttribute("ChargeArtwork", ""), new XAttribute("DisabledArtwork", ""), new XAttribute("HitArtwork", ""),
        new XAttribute("IdleSound", ""), new XAttribute("ChargeSound", ""), new XAttribute("HitSound", ""), new XAttribute("DisabledSound", ""),
        new XAttribute("SoundVolume", "1"), new XAttribute("ChargeFrames", "20"), new XAttribute("HitFrames", "8"),
        new XAttribute("EnabledByArea", "true"),
        new XAttribute("DangerX", "-90"), new XAttribute("DangerY", "-180"), new XAttribute("DangerWidth", "180"), new XAttribute("DangerHeight", "180"),
        new XAttribute("ActivationX", "-260"), new XAttribute("ActivationY", "-260"), new XAttribute("ActivationWidth", "520"), new XAttribute("ActivationHeight", "300"));

    public static string Save(string projectRoot, XElement manifest)
    {
        manifest = new XElement(manifest);
        ValidateManifest(manifest);
        foreach (var key in new[] { "Artwork", "ChargeArtwork", "DisabledArtwork", "HitArtwork" })
        {
            var filename = (string?)manifest.Attribute(key) ?? "";
            if (filename.Length == 0) continue;
            var source = ResolveArtwork(projectRoot, filename);
            using var stream = File.OpenRead(source);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("Trap artwork has no frames.");
        }
        var id = ProjectTemplateService.SafeId((string?)manifest.Attribute("ID") ?? "");
        if (id.Length == 0) throw new InvalidDataException("Give the trap a stable ID.");
        var package = Path.Combine(projectRoot, "custom_traps", id);
        var libraries = Path.Combine(projectRoot, "custom_gamedata", "run_data", "libraries");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(libraries);
        var libraryName = "v2trap_" + id + ".xml";
        manifest.SetAttributeValue("ID", id);
        manifest.SetAttributeValue("Library", libraryName);
        manifest.SetAttributeValue("Object", "V2Trap_" + id);
        CompileGifStates(projectRoot, package, manifest, id);
        var library = Compile(manifest);
        WriteXml(Path.Combine(package, "manifest.xml"), new XDocument(manifest));
        WriteXml(Path.Combine(package, libraryName), library);
        WriteXml(Path.Combine(libraries, libraryName), library);
        return Path.Combine(package, "manifest.xml");
    }

    private static void ValidateManifest(XElement manifest)
    {
        if (manifest.Name.LocalName != "CustomTrap") throw new InvalidDataException("Expected a CustomTrap manifest.");
        void Number(string key, int minimum, int maximum)
        {
            if (!int.TryParse((string?)manifest.Attribute(key), out var value) || value < minimum || value > maximum)
                throw new InvalidDataException($"{key} must be between {minimum} and {maximum}.");
        }
        foreach (var key in new[] { "Width", "Height" }) Number(key, 24, 1200);
        Number("DamageAmount", 1, 12);
        foreach (var key in new[] { "ChargeFrames", "HitFrames" }) Number(key, 0, 600);
        foreach (var prefix in new[] { "Danger", "Activation" })
        {
            Number(prefix + "X", -5000, 5000); Number(prefix + "Y", -5000, 5000);
            Number(prefix + "Width", 1, 5000); Number(prefix + "Height", 1, 5000);
        }
        if (!double.TryParse((string?)manifest.Attribute("SoundVolume"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var volume) || !double.IsFinite(volume) || volume is < 0 or > 1)
            throw new InvalidDataException("Sound volume must be between 0 and 1.");
    }

    private static string ResolveArtwork(string projectRoot, string filename)
    {
        if (Path.GetFileName(filename) != filename) throw new InvalidDataException("Choose artwork from Custom Textures.");
        var textures = Path.Combine(projectRoot, "custom_textures");
        var matches = Directory.Exists(textures) ? Directory.EnumerateFiles(textures, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals(filename, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray() : [];
        if (matches.Length != 1) throw new InvalidDataException(matches.Length == 0
            ? "Could not find trap artwork: " + filename : "Multiple textures use this filename: " + filename);
        return matches[0];
    }

    public static XDocument Compile(XElement manifest)
    {
        string Get(string name, string fallback = "") => (string?)manifest.Attribute(name) ?? fallback;
        int Number(string name, int fallback) => int.TryParse(Get(name), out var value) ? value : fallback;
        var id = ProjectTemplateService.SafeId(Get("ID"));
        var width = Math.Clamp(Number("Width", 180), 24, 1200);
        var height = Math.Clamp(Number("Height", 180), 24, 1200);
        var enabledByArea = Get("EnabledByArea", "true") != "false";
        var objectContent = new XElement("Content");
        foreach (var state in new[] { "Idle", "Charge", "Disabled", "Hit" })
        {
            var key = state == "Idle" ? "Artwork" : state + "Artwork";
            var filename = Get(key);
            var selected = filename.Length == 0 ? Get("Artwork") : filename;
            if (selected.Length == 0) continue;
            var isGif = Path.GetExtension(selected).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            var actualState = filename.Length == 0 ? "Idle" : state;
            var visual = new XElement(isGif ? "CustomAnimation" : "Image",
                new XAttribute("ClassName", isGif ? "v2trap_" + id + "_" + actualState.ToLowerInvariant() + ".xml" : Path.GetFileNameWithoutExtension(selected)),
                new XAttribute("X", -width / 2), new XAttribute("Y", -height), new XAttribute("Layer", "TrapsColor"),
                new XAttribute("Width", width), new XAttribute("Height", height), new XAttribute("Factor", "0"));
            if (isGif) { visual.SetAttributeValue("Speed", "30"); visual.SetAttributeValue("Iterations", "-1"); }
            var dynamic = new XElement("Dynamic");
            foreach (var visible in new[] { "Idle", "Charge", "Disabled", "Hit" })
                dynamic.Add(new XElement("Transformation", new XAttribute("Name", "Visual" + visible),
                    new XElement("ColorInterval", new XAttribute("Frames", "1"),
                        new XAttribute("ColorFinish", visible == state ? "#FFFFFFFF" : "#FFFFFF00"))));
            visual.Add(new XElement("Properties",
                new XElement("Static", new XElement("Matrix", new XAttribute("A", width), new XAttribute("B", "0"),
                    new XAttribute("C", "0"), new XAttribute("D", height), new XAttribute("Tx", "0"), new XAttribute("Ty", "0")),
                    new XElement("StartColor", new XAttribute("Color", state == "Idle" ? "#FFFFFFFF" : "#FFFFFF00"))),
                dynamic));
            objectContent.Add(visual);
        }

        if (enabledByArea)
        {
            var activation = Trigger("Activation Area", "Activation");
            var content = activation.Element("Content")!;
            var enter = new List<XElement>();
            AddSound(enter, Get("ChargeSound"), Get("SoundVolume", "1"));
            enter.Add(Action("Transform", "Name", "VisualCharge"));
            enter.Add(Action("Wait", "Frames", Math.Max(0, Number("ChargeFrames", 20)).ToString()));
            AddSound(enter, Get("IdleSound"), Get("SoundVolume", "1"));
            enter.Add(Action("Transform", "Name", "VisualIdle"));
            enter.Add(Action("Transform", "Name", "CustomTrapDangerOn"));
            content.Add(Loop("Enter", enter));
            var disabled = new List<XElement> { Action("Transform", "Name", "CustomTrapDangerOff") };
            AddSound(disabled, Get("DisabledSound"), Get("SoundVolume", "1"));
            disabled.Add(Action("Transform", "Name", "VisualDisabled"));
            content.Add(Loop("Exit", disabled));
            content.Add(Loop("OnHide", disabled.Select(item => new XElement(item))));
            objectContent.Add(activation);
        }

        var danger = Trigger("Danger Area", "Danger");
        var dangerActions = new List<XElement> { Action("Transform", "Name", "VisualHit") };
        AddSound(dangerActions, Get("HitSound"), Get("SoundVolume", "1"));
        switch (Get("Impact", "Instant defeat"))
        {
            case "Damage armour":
                dangerActions.Add(new XElement("ArmorDamage", new XAttribute("Model", "Player"),
                    new XAttribute("Amount", Math.Clamp(Number("DamageAmount", 1), 1, 12)),
                    new XAttribute("Slot", Get("ArmorSlot", "Torso"))));
                break;
            case "Knockback only":
                dangerActions.Add(new XElement("Impulse", new XAttribute("Model", "Player"),
                    new XAttribute("Impulse", "40"), new XAttribute("R", "1000"), new XAttribute("Absorption", "0.8")));
                break;
            default: dangerActions.Add(Action("Kill", "Model", "Player")); break;
        }
        dangerActions.Add(Action("Wait", "Frames", Math.Max(0, Number("HitFrames", 8)).ToString()));
        dangerActions.Add(Action("Transform", "Name", "VisualIdle"));
        danger.Element("Content")!.Add(Loop("Enter", dangerActions));
        if (enabledByArea)
        {
            var dynamic = new XElement("Dynamic");
            foreach (var (name, type) in new[] { ("CustomTrapDangerOn", "On"), ("CustomTrapDangerOff", "Off") })
                dynamic.Add(new XElement("Transformation", new XAttribute("Name", name),
                    new XElement("ActivationInterval", new XAttribute("Type", type))));
            danger.Add(new XElement("Properties", new XElement("Static",
                new XElement("Enable", new XAttribute("Value", "0"))), dynamic));
        }
        objectContent.Add(danger);
        var variables = new XElement("ContentVariable");
        foreach (var key in new[] { "DangerX", "DangerY", "DangerWidth", "DangerHeight",
            "ActivationX", "ActivationY", "ActivationWidth", "ActivationHeight" })
            variables.Add(new XElement("Variable", new XAttribute("Name", key), new XAttribute("Type", "E_Int"),
                new XAttribute("Default", Get(key, "0")), new XAttribute("AvailableTypes", "E_Int")));
        variables.Add(new XElement("Variable", new XAttribute("Name", "EnableArea"),
            new XAttribute("Default", enabledByArea ? "1" : "0")));
        var obj = new XElement("Object", new XAttribute("Name", "V2Trap_" + id), new XAttribute("X", "0"),
            new XAttribute("Y", "0"), new XAttribute("EditorTitle", Get("Name")), new XAttribute("EditorFamily", "Custom"),
            objectContent, new XElement("Properties", new XElement("Static", variables)));
        return new XDocument(new XElement("Root", new XElement("Objects", obj)));

        XElement Trigger(string name, string prefix) => new("Trigger", new XAttribute("Name", name),
            new XAttribute("X", "~" + prefix + "X"), new XAttribute("Y", "~" + prefix + "Y"),
            new XAttribute("Width", "~" + prefix + "Width"), new XAttribute("Height", "~" + prefix + "Height"),
            TriggerContent());
    }

    private static XElement TriggerContent() => new("Content",
        new XElement("Init", new XElement("SetVariable", new XAttribute("Name", "$AI"), new XAttribute("Type", "AI"), new XAttribute("Value", "0")),
            new XElement("SetVariable", new XAttribute("Name", "$Active"), new XAttribute("Type", "Bool"), new XAttribute("Value", "1")),
            new XElement("SetVariable", new XAttribute("Name", "$Node"), new XAttribute("Type", "Node"), new XAttribute("Value", "COM"))));
    private static XElement Loop(string eventName, IEnumerable<XElement> actions) => new("Loop",
        new XElement("Events", new XElement(eventName)), new XElement("Actions", actions));
    private static XElement Action(string name, string key, string value) => new(name, new XAttribute(key, value));
    private static void AddSound(List<XElement> actions, string name, string volume)
    {
        if (name.Length == 0) return;
        var amount = double.TryParse(volume, System.Globalization.CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 0, 1) : 1;
        actions.Add(new XElement("Sound", new XAttribute("Action", "Play"), new XAttribute("Channel", "Sound"),
            new XAttribute("Name", name), new XAttribute("Volume", amount.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static void CompileGifStates(string projectRoot, string package, XElement manifest, string id)
    {
        var textureRoot = Path.Combine(projectRoot, "custom_textures");
        foreach (var state in new[] { "Idle", "Charge", "Disabled", "Hit" })
        {
            var key = state == "Idle" ? "Artwork" : state + "Artwork";
            var filename = (string?)manifest.Attribute(key) ?? "";
            if (!Path.GetExtension(filename).Equals(".gif", StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(filename) != filename) throw new InvalidDataException("Choose artwork from Custom Textures.");
            var source = ResolveArtwork(projectRoot, filename);
            if (!File.Exists(source)) throw new FileNotFoundException("Could not find trap GIF", source);
            using var stream = File.OpenRead(source);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("Trap GIF has no frames.");
            var frames = new XElement("CustomAnimation");
            var textureFolder = Path.Combine(textureRoot, "v2trap_" + id);
            Directory.CreateDirectory(textureFolder);
            for (var index = 0; index < decoder.Frames.Count; index++)
            {
                var frameName = state.ToLowerInvariant() + "_" + index.ToString("D4") + ".png";
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(decoder.Frames[index]);
                using (var output = File.Create(Path.Combine(textureFolder, frameName))) encoder.Save(output);
                var duration = 1;
                try
                {
                    var metadata = decoder.Frames[index].Metadata as BitmapMetadata;
                    if (metadata?.GetQuery("/grctlext/Delay") is ushort delay)
                        duration = Math.Max(1, (int)Math.Round(delay * 0.3));
                }
                catch { }
                frames.Add(new XElement("Frame", new XAttribute("Texture", "v2trap_" + id + "/" + frameName),
                    new XAttribute("Frames", duration)));
            }
            var animationFolder = Path.Combine(package, "animations");
            Directory.CreateDirectory(animationFolder);
            WriteXml(Path.Combine(animationFolder, "v2trap_" + id + "_" + state.ToLowerInvariant() + ".xml"), new XDocument(frames));
        }
    }

    private static void WriteXml(string path, XDocument document)
    {
        var temporary = path + ".tmp";
        document.Save(temporary);
        File.Move(temporary, path, true);
    }
}
