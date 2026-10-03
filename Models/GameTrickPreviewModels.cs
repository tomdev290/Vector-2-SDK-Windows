using System.Windows;

namespace Vector2LevelEditor.Models;

public sealed record GameTrickMove(
    string Name,
    string FileName,
    int FirstFrame,
    int EndFrame,
    int MidFrames,
    bool Loops,
    string PivotNode,
    bool IsTrick,
    string Parts)
{
    public override string ToString() => Name;
}

public sealed record GameTrickModelSkin(string Filename, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record GameTrickNode(string Name, Point BasePoint, int? AnimationIndex, IReadOnlyList<string> ChildNames, IReadOnlyList<double> ChildWeights);
public sealed record GameTrickEdge(string Name, string StartName, string EndName);
public sealed record GameTrickCapsule(string EdgeName, double Radius, double Margin1, double Margin2);
public sealed record GameTrickTriangle(IReadOnlyList<string> NodeNames);
public sealed record GameTrickNodePoint(string NodeName, double Radius);

public sealed class GameTrickModel
{
    public required IReadOnlyList<GameTrickNode> Nodes { get; init; }
    public required IReadOnlyList<GameTrickEdge> Edges { get; init; }
    public required IReadOnlyList<GameTrickCapsule> Capsules { get; init; }
    public required IReadOnlyList<GameTrickTriangle> Triangles { get; init; }
    public required IReadOnlyList<GameTrickNodePoint> NodePoints { get; init; }

    public Dictionary<string, Point> ResolvePoints(IReadOnlyList<Point> frame)
    {
        var byName = Nodes.ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        Point Resolve(GameTrickNode node, HashSet<string> stack)
        {
            if (resolved.TryGetValue(node.Name, out var cached)) return cached;
            if (!stack.Add(node.Name)) return node.BasePoint;
            Point value;
            if (node.AnimationIndex is int index && index >= 0 && index < frame.Count)
            {
                value = frame[index];
            }
            else if (node.ChildNames.Count > 0)
            {
                double x = 0, y = 0, total = 0;
                for (var i = 0; i < node.ChildNames.Count; i++)
                {
                    if (!byName.TryGetValue(node.ChildNames[i], out var child)) continue;
                    var weight = i < node.ChildWeights.Count ? node.ChildWeights[i] : 1;
                    var point = Resolve(child, stack);
                    x += point.X * weight; y += point.Y * weight; total += weight;
                }
                value = Math.Abs(total) > 0.0001 ? new Point(x / total, y / total) : node.BasePoint;
            }
            else value = node.BasePoint;
            stack.Remove(node.Name);
            return resolved[node.Name] = value;
        }
        foreach (var node in Nodes) Resolve(node, []);
        return resolved;
    }
}

public sealed class GameTrickPlayback
{
    public required GameTrickMove Move { get; init; }
    public required GameTrickModel Model { get; init; }
    public required IReadOnlyList<IReadOnlyList<Point>> Frames { get; init; }
    public int StartFrame { get; init; }
    public IReadOnlyList<string> SelectedSkins { get; init; } = [];
    public Guid? AnchorNodeId { get; init; }
    public Vector PlacementOffset { get; set; }

    public GameTrickSample Sample(int tick)
    {
        if (Frames.Count == 0) return new GameTrickSample(0, 0, []);
        var first = Math.Clamp(StartFrame, 0, Frames.Count - 1);
        var requestedEnd = Move.EndFrame > 0 ? Move.EndFrame : Frames.Count - 1;
        var end = Math.Max(first, Math.Min(requestedEnd, Frames.Count - 1));
        var span = Math.Max(1, end - first + 1);
        var pointFrames = Math.Max(1, Move.MidFrames);
        var step = pointFrames + 1;
        var safeTick = Math.Max(0, tick);
        var frameIndex = first + ((safeTick / step) % span);
        var subframe = safeTick % step;
        var previousIndex = frameIndex == first ? end : Math.Max(first, frameIndex - 1);
        var nextIndex = frameIndex == end ? first : Math.Min(end, frameIndex + 1);
        var previous = Frames[previousIndex];
        var current = Frames[frameIndex];
        var next = Frames[nextIndex];
        var count = Math.Min(previous.Count, Math.Min(current.Count, next.Count));
        var t = (Math.Clamp(subframe, 0, pointFrames) + 1.0) / (pointFrames + 1.0);
        var pose = new Point[count];
        for (var index = 0; index < count; index++)
        {
            var start = Lerp(previous[index], current[index], .5);
            var finish = Lerp(current[index], next[index], .5);
            pose[index] = Quadratic(start, current[index], finish, t);
        }
        return new GameTrickSample(frameIndex, subframe, pose);
    }

    private static Point Lerp(Point left, Point right, double t)
        => new(left.X + (right.X - left.X) * t, left.Y + (right.Y - left.Y) * t);

    private static Point Quadratic(Point left, Point middle, Point right, double t)
    {
        var clamped = Math.Clamp(t, 0, 1);
        var inverse = 1 - clamped;
        return new Point(
            left.X * inverse * inverse + middle.X * 2 * clamped * inverse + right.X * clamped * clamped,
            left.Y * inverse * inverse + middle.Y * 2 * clamped * inverse + right.Y * clamped * clamped);
    }
}

public sealed record GameTrickSample(int FrameIndex, int Subframe, IReadOnlyList<Point> Pose);
