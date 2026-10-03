using System.Xml.Linq;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

var root = Path.Combine(Path.GetTempPath(), $"Vector2BackgroundIsolation-{Guid.NewGuid():N}");
var folderA = Path.Combine(root, "A");
var folderB = Path.Combine(root, "B");
Directory.CreateDirectory(folderA);
Directory.CreateDirectory(folderB);

try
{
    BackgroundDesignerService.SaveCurrent(folderA,
        new CustomBackgroundDefinition
        {
            Name = "alpha",
            Pieces = ["<Image Name=\"alpha_piece\" />"]
        });

    var emptyB = BackgroundDesignerService.LoadCatalog(folderB);
    if (emptyB.Definitions.Count != 0)
        throw new InvalidOperationException("A folder without XML did not load as an empty catalog.");

    BackgroundDesignerService.SaveCurrent(folderB,
        new CustomBackgroundDefinition
        {
            Name = "beta",
            Pieces = ["<Image Name=\"beta_piece\" />"]
        });
    BackgroundDesignerService.SaveCurrent(folderA,
        new CustomBackgroundDefinition
        {
            Name = "replacement",
            Pieces = ["<Image Name=\"replacement_piece\" />"]
        });

    var reloadedA = BackgroundDesignerService.LoadCatalog(folderA);
    var reloadedB = BackgroundDesignerService.LoadCatalog(folderB);
    if (reloadedA.Definitions.Select(item => item.Name).Single() != "replacement")
        throw new InvalidOperationException("Saving did not replace the previous background in folder A.");
    if (reloadedB.Definitions.Select(item => item.Name).Single() != "beta")
        throw new InvalidOperationException("Folder B was contaminated while saving folder A.");

    var savedXml = XDocument.Load(Path.Combine(folderA, "custom_backgrounds.xml"));
    if (savedXml.Root?.Elements("Background").Count() != 1)
        throw new InvalidOperationException("The saved catalog contains more than one background.");
    if (savedXml.Root!.DescendantsAndSelf().Attributes().Any(attribute =>
            attribute.Name.LocalName.Contains("Random", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("Random background metadata is still being written.");
    if (Directory.EnumerateFiles(folderA, "*.tmp").Any() || Directory.EnumerateFiles(folderB, "*.tmp").Any())
        throw new InvalidOperationException("Atomic save left a temporary file behind.");

    var packagedRoot = Path.Combine(root, "packaged-editor");
    var bundledGameFolder = Path.Combine(packagedRoot, "Vector 2 Game");
    Directory.CreateDirectory(bundledGameFolder);
    var bundledExecutable = Path.Combine(bundledGameFolder, "Vector 2.exe");
    File.WriteAllText(bundledExecutable, "verification placeholder");
    var resolvedExecutable = Vector2PlayBridge.ResolveGameExecutable("", packagedRoot);
    if (!resolvedExecutable.Equals(bundledExecutable, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The packaged Vector 2 executable was not discovered beside the editor.");

    Console.WriteLine("Single-background isolation, no-random XML, and bundled game discovery verified.");
}
finally
{
    Directory.Delete(root, true);
}
