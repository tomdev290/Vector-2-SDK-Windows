using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;
using System.Xml.Linq;

var vm = new MainWindowViewModel();
var factor = vm.SelectedDocument.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor);
var parent = new LevelNode { Name = "Parent", Kind = LevelNodeKind.Object };
var first = new LevelNode { Name = "First", Kind = LevelNodeKind.Platform };
var second = new LevelNode { Name = "Second", Kind = LevelNodeKind.Trigger };
factor.Children.Add(parent);
factor.Children.Add(first);
factor.Children.Add(second);

vm.SelectNode(first);
vm.ToggleNodeSelection(second);
vm.ReparentSelection(first, parent);
Require(parent.Children.Contains(first) && parent.Children.Contains(second), "multi-selection did not move under the parent");
Require(first.IsHierarchySelected && second.IsHierarchySelected, "hierarchy selection state did not survive parenting");
Require(first.IsHierarchyAttachment && second.IsHierarchyAttachment, "parented nodes were not marked as macOS-style attachments");

var beforeMove = (FirstX: first.X, FirstY: first.Y, SecondX: second.X, SecondY: second.Y);
vm.SelectNode(parent);
vm.MoveSelected(30, -20);
Require((first.X, first.Y, second.X, second.Y) ==
        (beforeMove.FirstX + 30, beforeMove.FirstY - 20, beforeMove.SecondX + 30, beforeMove.SecondY - 20),
    "group movement did not carry attached children");

var exported = XDocument.Parse(new Exporter().Export(vm.SelectedDocument));
var exportedParent = exported.Descendants("Object").FirstOrDefault(element => (string?)element.Attribute("Name") == "Parent");
Require(exportedParent is not null, "group parent was not exported");
var groupedContent = exportedParent!.Element("Content")?.Elements().ToList() ?? [];
Require(groupedContent.Count(element => element.Name.LocalName == "Platform") == 1, "attached platform was not exported once");
Require(groupedContent.Count(element => element.Name.LocalName == "Trigger") == 1, "attached trigger was not exported once");

vm.SelectNode(first);
vm.ToggleNodeSelection(second);
vm.ReparentSelection(first, null);
Require(factor.Children.Contains(first) && factor.Children.Contains(second), "root drop did not restore both nodes to the scene Factor");
Require(!first.IsHierarchyAttachment && !second.IsHierarchyAttachment, "unparenting did not clear attachment metadata");

var child = new LevelNode { Name = "Child", Kind = LevelNodeKind.Area };
first.Children.Add(child);
child.MarkHierarchyAttachment(true);
var wrappedPlatform = XElement.Parse(new Exporter().ExportSelection(first));
Require(wrappedPlatform.Name.LocalName == "Object", "non-container parent did not get the macOS Object wrapper");
Require(wrappedPlatform.Element("Content")?.Elements("Platform").Count() == 1, "wrapped parent body was not exported once");
Require(wrappedPlatform.Element("Content")?.Elements("Area").Count() == 1, "wrapped attached child was not exported once");

var reference = new LevelNode
{
    Name = "LibraryOwner",
    Kind = LevelNodeKind.ObjectReference,
    Filename = "objects.xml",
    X = 400,
    Y = 100
};
reference.Children.Add(new LevelNode
{
    Name = "PreviewArt",
    Kind = LevelNodeKind.Image,
    IsPreviewOnly = true,
    ImagePath = "preview.png"
});
var referenceChild = new LevelNode { Name = "AttachedArea", Kind = LevelNodeKind.Area, X = 460, Y = 125 };
referenceChild.MarkHierarchyAttachment(true);
reference.Children.Add(referenceChild);
var wrappedReference = XElement.Parse(new Exporter().ExportSelection(reference));
var referenceContent = wrappedReference.Element("Content")?.Elements().ToList() ?? [];
Require(wrappedReference.Name.LocalName == "Object", "object-reference parent did not get a group wrapper");
Require(referenceContent.Count(element => element.Name.LocalName == "ObjectReference") == 1, "object-reference body was not exported once");
Require(referenceContent.Count(element => element.Name.LocalName == "Area") == 1, "object-reference attached child was not exported once");
Require(referenceContent.All(element => element.Name.LocalName != "Image"), "preview-only artwork leaked into grouped XML");

var legacyReference = new LevelNode
{
    Name = "LegacyLibraryOwner",
    Kind = LevelNodeKind.ObjectReference,
    Filename = "objects.xml",
    X = 600,
    Y = 200,
    DynamicXml = "<Dynamic><Transformation Name=\"MoveTogether\"><MoveInterval Frames=\"30\" Type=\"Bezier\"><Point X=\"0\" Y=\"0\"/><Point X=\"100\" Y=\"0\"/></MoveInterval></Transformation></Dynamic>"
};
var legacyCollision = new LevelNode
{
    Name = "LegacyCollision",
    Kind = LevelNodeKind.Platform,
    X = 640,
    Y = 240,
    Width = 120,
    Height = 30
};
legacyReference.Children.Add(legacyCollision); // Simulates a pre-attachment-marker grouped room.
factor.Children.Add(legacyReference);
vm.SelectNode(legacyReference);
vm.MoveSelected(25, 15);
Require((legacyCollision.X, legacyCollision.Y) == (665, 255), "collision child did not follow an object-reference parent");
var legacyXml = XElement.Parse(new Exporter().ExportSelection(legacyReference));
Require(legacyXml.Name.LocalName == "Object", "legacy grouped object-reference was not wrapped");
Require(legacyXml.Element("Content")?.Elements("Platform").Count() == 1, "legacy collision was not retained inside the animated parent");
Require(legacyXml.Element("Properties")?.Element("Dynamic") is not null, "dynamic motion was not attached to the shared group wrapper");

var promotedPreviewCollision = new LevelNode
{
    Name = "PromotedCollision",
    Kind = LevelNodeKind.Platform,
    IsPreviewOnly = true,
    X = 700,
    Y = 200
};
factor.Children.Add(promotedPreviewCollision);
vm.SelectNode(promotedPreviewCollision);
vm.ReparentSelection(promotedPreviewCollision, reference);
Require(!promotedPreviewCollision.IsPreviewOnly && promotedPreviewCollision.IsHierarchyAttachment,
    "explicitly parented collision remained preview-only");

var overlappingImage = new LevelNode
{
    Name = "black_low__black.v_black",
    Kind = LevelNodeKind.Image,
    X = 900,
    Y = 300,
    Width = 160,
    Height = 160,
    ImagePath = "black.v"
};
var overlappingCollision = new LevelNode
{
    Name = "Platform",
    Kind = LevelNodeKind.Platform,
    X = 900,
    Y = 300,
    Width = 160,
    Height = 160
};
factor.Children.Add(overlappingImage);
factor.Children.Add(overlappingCollision);
vm.SelectNode(overlappingCollision);
vm.ReparentSelection(overlappingCollision, overlappingImage);
var canvasTarget = vm.ResolveCanvasDragTarget(overlappingCollision);
Require(canvasTarget.Id == overlappingImage.Id, "overlapping collision did not resolve to its canvas group owner");
vm.SelectNode(canvasTarget);
vm.MoveSelected(80, -40);
Require((overlappingImage.X, overlappingImage.Y) == (980, 260), "canvas drag did not move the image parent");
Require((overlappingCollision.X, overlappingCollision.Y) == (980, 260), "canvas drag did not carry the overlapping collision child");
vm.SelectNode(first);
vm.ReparentSelection(first, child);
Require(factor.Children.Contains(first) && first.Children.Contains(child), "descendant-cycle guard failed");

Console.WriteLine("Hierarchy parenting smoke test passed.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
