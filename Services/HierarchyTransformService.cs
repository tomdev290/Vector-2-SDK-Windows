using System.Windows;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Applies a parent transform while carrying world-space descendants exactly as
/// the macOS editor does. Child sizes stay unchanged; translation and rotation
/// follow the transformed parent hierarchy.
/// </summary>
public static class HierarchyTransformService
{
    public readonly record struct NodeTransform(int X, int Y, int Width, int Height, double Rotation);

    public static Dictionary<Guid, NodeTransform> Capture(LevelNode root)
        => root.Flatten().ToDictionary(node => node.Id, FromNode);

    public static void Restore(LevelNode root, IReadOnlyDictionary<Guid, NodeTransform> snapshot)
    {
        foreach (var node in root.Flatten())
        {
            if (snapshot.TryGetValue(node.Id, out var transform))
            {
                SetNode(node, transform);
            }
        }
    }

    public static void ApplyFromSnapshot(
        LevelNode root,
        IReadOnlyDictionary<Guid, NodeTransform> snapshot,
        NodeTransform target)
    {
        if (!snapshot.TryGetValue(root.Id, out var originalRoot)) return;

        // Every preview frame starts from the same source pose. This prevents
        // child transforms accumulating rounding error while the user scrubs.
        Restore(root, snapshot);
        SetNode(root, target);
        TransformDescendants(root, originalRoot, target);
    }

    public static NodeTransform FromNode(LevelNode node)
        => new(node.X, node.Y, node.Width, node.Height, node.Rotation);

    private static void TransformDescendants(LevelNode parent, NodeTransform oldParent, NodeTransform newParent)
    {
        if (parent.Children.Count == 0) return;

        var oldAnchor = TransformAnchor(parent, oldParent);
        var newAnchor = TransformAnchor(parent, newParent);
        var rotationDelta = newParent.Rotation - oldParent.Rotation;
        var radians = rotationDelta * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var translationX = newAnchor.X - oldAnchor.X;
        var translationY = newAnchor.Y - oldAnchor.Y;

        if (Math.Abs(translationX) <= 0.001 &&
            Math.Abs(translationY) <= 0.001 &&
            Math.Abs(rotationDelta) <= 0.001)
        {
            return;
        }

        foreach (var child in parent.Children)
        {
            var oldChild = FromNode(child);
            var oldChildAnchor = TransformAnchor(child, oldChild);
            var translatedX = oldChildAnchor.X + translationX;
            var translatedY = oldChildAnchor.Y + translationY;
            var relativeX = translatedX - newAnchor.X;
            var relativeY = translatedY - newAnchor.Y;
            var rotatedAnchor = new Point(
                newAnchor.X + relativeX * cosine - relativeY * sine,
                newAnchor.Y + relativeX * sine + relativeY * cosine);

            var baseCenterX = rotatedAnchor.X - child.VisualOffsetX;
            var baseCenterY = rotatedAnchor.Y - child.VisualOffsetY;
            var newChild = UsesVectorRectOrigin(child.Kind)
                ? oldChild with
                {
                    X = (int)Math.Round(baseCenterX - oldChild.Width / 2.0),
                    Y = (int)Math.Round(baseCenterY - oldChild.Height / 2.0),
                    Rotation = oldChild.Rotation + rotationDelta
                }
                : oldChild with
                {
                    X = (int)Math.Round(baseCenterX),
                    Y = (int)Math.Round(baseCenterY),
                    Rotation = oldChild.Rotation + rotationDelta
                };

            SetNode(child, newChild);
            TransformDescendants(child, oldChild, newChild);
        }
    }

    private static Point TransformAnchor(LevelNode node, NodeTransform transform)
    {
        var x = UsesVectorRectOrigin(node.Kind) ? transform.X + transform.Width / 2.0 : transform.X;
        var y = UsesVectorRectOrigin(node.Kind) ? transform.Y + transform.Height / 2.0 : transform.Y;
        return new Point(x + node.VisualOffsetX, y + node.VisualOffsetY);
    }

    private static bool UsesVectorRectOrigin(LevelNodeKind kind)
        => kind is LevelNodeKind.Image
            or LevelNodeKind.Platform
            or LevelNodeKind.Trapezoid
            or LevelNodeKind.Trigger
            or LevelNodeKind.Area
            or LevelNodeKind.Comment;

    private static void SetNode(LevelNode node, NodeTransform transform)
    {
        node.X = transform.X;
        node.Y = transform.Y;
        node.Width = transform.Width;
        node.Height = transform.Height;
        node.Rotation = transform.Rotation;
    }
}
