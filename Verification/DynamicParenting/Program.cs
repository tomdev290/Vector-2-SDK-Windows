using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

var parent = new LevelNode
{
    Name = "Parent",
    Kind = LevelNodeKind.Object,
    X = 100,
    Y = 100,
    Width = 80,
    Height = 80
};
var child = new LevelNode
{
    Name = "Child",
    Kind = LevelNodeKind.Platform,
    X = 140,
    Y = 110,
    Width = 40,
    Height = 20
};
var grandchild = new LevelNode
{
    Name = "Grandchild",
    Kind = LevelNodeKind.Trigger,
    X = 150,
    Y = 115,
    Width = 10,
    Height = 10
};
parent.Children.Add(child);
child.Children.Add(grandchild);

var original = HierarchyTransformService.Capture(parent);
HierarchyTransformService.ApplyFromSnapshot(parent, original, new(200, 250, 120, 100, 0));
Require((child.X, child.Y) == (240, 260), "child did not follow parent translation");
Require((grandchild.X, grandchild.Y) == (250, 265), "grandchild did not follow parent translation");
Require((child.Width, child.Height) == (40, 20), "dynamic parent resize changed child size");

// Applying the same absolute frame again must not accumulate movement.
HierarchyTransformService.ApplyFromSnapshot(parent, original, new(200, 250, 120, 100, 0));
Require((child.X, child.Y) == (240, 260), "repeated preview frame drifted the child");

HierarchyTransformService.ApplyFromSnapshot(parent, original, new(100, 100, 80, 80, 90));
Require((child.X, child.Y, child.Rotation) == (60, 150, 90), "child did not rotate with parent");
Require((grandchild.X, grandchild.Y, grandchild.Rotation) == (75, 150, 90), "grandchild did not rotate with parent");

HierarchyTransformService.Restore(parent, original);
Require((parent.X, parent.Y, parent.Rotation) == (100, 100, 0), "parent restore failed");
Require((child.X, child.Y, child.Rotation) == (140, 110, 0), "child restore failed");
Require((grandchild.X, grandchild.Y, grandchild.Rotation) == (150, 115, 0), "grandchild restore failed");

Console.WriteLine("Dynamic hierarchy transform smoke test passed.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
