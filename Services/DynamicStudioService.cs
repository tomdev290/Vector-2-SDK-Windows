using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class DynamicStudioService
{
    private static readonly JsonSerializerOptions TimelineJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GenerateDynamicXml(
        string transformName,
        IEnumerable<DynamicStudioKeyframe> frames,
        DynamicPathMode movementMode,
        DynamicSinQuarter movementQuarter,
        DynamicPathMode sizeMode,
        DynamicSinQuarter sizeQuarter,
        DynamicRotationMode rotationMode)
    {
        var ordered = frames.OrderBy(frame => frame.Frame).ToList();
        if (ordered.Count < 2) return "";

        var transformation = new XElement("Transformation", new XAttribute("Name", SafeName(transformName)));
        foreach (var (start, finish) in ordered.Zip(ordered.Skip(1)))
        {
            var intervalFrames = Math.Max(1, finish.Frame - start.Frame);
            var wroteInterval = false;
            var dx = finish.X - start.X;
            var dy = finish.Y - start.Y;
            if (dx != 0 || dy != 0)
            {
                var move = new XElement("MoveInterval",
                    new XAttribute("Frames", intervalFrames),
                    new XAttribute("Type", PathXmlName(movementMode)),
                    new XElement("Point", new XAttribute("X", 0), new XAttribute("Y", 0)),
                    new XElement("Point", new XAttribute("X", dx), new XAttribute("Y", dy)));
                AddQuarter(move, movementMode, movementQuarter);
                transformation.Add(move);
                wroteInterval = true;
            }

            var rotationDelta = finish.Rotation - start.Rotation;
            if (Math.Abs(rotationDelta) > 0.001)
            {
                transformation.Add(new XElement("RotationInterval",
                    new XAttribute("Angle", Pretty(rotationDelta)),
                    new XAttribute("Type", RotationXmlName(rotationMode)),
                    new XAttribute("Frames", intervalFrames)));
                wroteInterval = true;
            }

            var widthRatio = start.Width == 0 ? 1 : (double)finish.Width / start.Width;
            var heightRatio = start.Height == 0 ? 1 : (double)finish.Height / start.Height;
            if (Math.Abs(widthRatio - 1) > 0.001 || Math.Abs(heightRatio - 1) > 0.001)
            {
                var size = new XElement("SizeInterval",
                    new XAttribute("Frames", intervalFrames),
                    new XAttribute("Type", PathXmlName(sizeMode)),
                    new XElement("Point", new XAttribute("W", 1), new XAttribute("H", 1)),
                    new XElement("Point", new XAttribute("W", Pretty(widthRatio)), new XAttribute("H", Pretty(heightRatio))));
                AddQuarter(size, sizeMode, sizeQuarter);
                transformation.Add(size);
                wroteInterval = true;
            }

            if (!wroteInterval)
            {
                transformation.Add(new XElement("DelayInterval", new XAttribute("Frames", intervalFrames)));
            }
        }

        return new XElement("Dynamic", transformation).ToString();
    }

    public static string GenerateTriggerLoop(string transformName)
    {
        var safeName = SafeName(transformName);
        return new XElement("Loop",
            new XAttribute("Name", $"Run_{safeName}"),
            new XElement("Events", new XElement("Enter")),
            new XElement("Actions",
                new XElement("Choose",
                    new XAttribute("Order", "Sync"),
                    new XAttribute("Set", "0"),
                    new XElement("Transform", new XAttribute("Name", safeName))))).ToString();
    }

    public static DynamicStudioKeyframe? Interpolate(
        IEnumerable<DynamicStudioKeyframe> frames,
        int frame,
        DynamicPathMode movementMode,
        DynamicSinQuarter movementQuarter,
        DynamicPathMode sizeMode,
        DynamicSinQuarter sizeQuarter,
        DynamicRotationMode rotationMode)
    {
        var ordered = frames.OrderBy(keyframe => keyframe.Frame).ToList();
        if (ordered.Count == 0) return null;
        if (ordered.Count == 1 || frame <= ordered[0].Frame) return CopyAt(ordered[0], frame);
        if (frame >= ordered[^1].Frame) return CopyAt(ordered[^1], frame);

        var upperIndex = ordered.FindIndex(keyframe => keyframe.Frame >= frame);
        if (upperIndex <= 0) return CopyAt(ordered[0], frame);
        var lower = ordered[upperIndex - 1];
        var upper = ordered[upperIndex];
        var raw = (double)(frame - lower.Frame) / Math.Max(1, upper.Frame - lower.Frame);
        var moveT = PathProgress(raw, movementMode, movementQuarter);
        var sizeT = PathProgress(raw, sizeMode, sizeQuarter);
        var rotationT = RotationProgress(raw, rotationMode);
        return new DynamicStudioKeyframe
        {
            Frame = frame,
            X = Lerp(lower.X, upper.X, moveT),
            Y = Lerp(lower.Y, upper.Y, moveT),
            Width = Lerp(lower.Width, upper.Width, sizeT),
            Height = Lerp(lower.Height, upper.Height, sizeT),
            Rotation = Lerp(lower.Rotation, upper.Rotation, rotationT)
        };
    }

    public static string EncodeTimeline(DynamicStudioTimelinePackage package)
        => JsonSerializer.Serialize(package, TimelineJsonOptions);

    public static DynamicStudioTimelinePackage? DecodeTimeline(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<DynamicStudioTimelinePackage>(raw, TimelineJsonOptions); }
        catch { return null; }
    }

    public static string TransformNameFromXml(string raw, string fallback)
    {
        try
        {
            var dynamic = ParseDynamic(raw);
            return (string?)dynamic?.Element("Transformation")?.Attribute("Name") ?? SafeName(fallback);
        }
        catch { return SafeName(fallback); }
    }

    public static DynamicStudioTimelinePackage? ImportTimeline(LevelNode node)
    {
        try
        {
            var transformation = ParseDynamic(node.DynamicXml)?.Element("Transformation");
            if (transformation is null) return null;
            var package = new DynamicStudioTimelinePackage { Name = (string?)transformation.Attribute("Name") ?? SafeName(node.Name) };
            var current = new DynamicStudioKeyframe { Frame = 0, X = node.X, Y = node.Y, Width = node.Width, Height = node.Height, Rotation = node.Rotation };
            package.Keyframes.Add(current);
            foreach (var interval in transformation.Elements())
            {
                if (!int.TryParse((string?)interval.Attribute("Frames"), out var duration) || duration < 1) return null;
                var next = CopyAt(current, checked(current.Frame + duration));
                double Number(XElement point, string name, double fallback = 0)
                    => double.TryParse((string?)point.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
                var points = interval.Elements("Point").ToList();
                switch (interval.Name.LocalName)
                {
                    case "MoveInterval" when points.Count == 2:
                        next.X += (int)Math.Round(Number(points[1], "X") - Number(points[0], "X"));
                        next.Y += (int)Math.Round(Number(points[1], "Y") - Number(points[0], "Y"));
                        package.MovementMode = (string?)interval.Attribute("Type") == "Sin" ? DynamicPathMode.Sin : DynamicPathMode.Bezier;
                        package.MovementQuarter = ImportQuarter(interval);
                        break;
                    case "SizeInterval" when points.Count == 2:
                        next.Width = (int)Math.Round(current.Width * Number(points[1], "W", 1));
                        next.Height = (int)Math.Round(current.Height * Number(points[1], "H", 1));
                        package.SizeMode = (string?)interval.Attribute("Type") == "Sin" ? DynamicPathMode.Sin : DynamicPathMode.Bezier;
                        package.SizeQuarter = ImportQuarter(interval);
                        break;
                    case "RotationInterval":
                        next.Rotation += Number(interval, "Angle");
                        var mode = Enum.GetValues<DynamicRotationMode>().Cast<DynamicRotationMode?>()
                            .FirstOrDefault(value => RotationXmlName(value!.Value) == ((string?)interval.Attribute("Type") ?? "Linear"));
                        if (mode is null) return null;
                        package.RotationMode = mode.Value;
                        break;
                    case "DelayInterval": break;
                    default: return null; // Keep unsupported curves as original XML instead of approximating them.
                }
                package.Keyframes.Add(next);
                current = next;
            }
            package.TotalFrames = Math.Max(1, current.Frame);
            return package.Keyframes.Count > 1 ? package : null;
        }
        catch (Exception error) when (error is System.Xml.XmlException or OverflowException) { return null; }
    }

    private static DynamicSinQuarter ImportQuarter(XElement interval)
        => (string?)interval.Element("Quarters")?.Attribute("Value") switch
        {
            "1QuarterAcc" => DynamicSinQuarter.EaseIn,
            "1QuarterDec" => DynamicSinQuarter.EaseOut,
            "2QuartersFastSlowFast" => DynamicSinQuarter.FastSlowFast,
            "3Quarters" => DynamicSinQuarter.BacktrackThenFinish,
            _ => DynamicSinQuarter.EaseInOut
        };

    public static string SafeName(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), "[^A-Za-z0-9_-]", "_").Trim('_', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "NewTransform" : sanitized;
    }

    private static XElement? ParseDynamic(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var root = XElement.Parse(raw.Trim());
        return root.Name.LocalName == "Dynamic" ? root : root.Element("Dynamic");
    }

    private static DynamicStudioKeyframe CopyAt(DynamicStudioKeyframe source, int frame) => new()
    {
        Frame = frame,
        X = source.X,
        Y = source.Y,
        Width = source.Width,
        Height = source.Height,
        Rotation = source.Rotation
    };

    private static void AddQuarter(XElement interval, DynamicPathMode mode, DynamicSinQuarter quarter)
    {
        if (mode == DynamicPathMode.Sin)
        {
            interval.Add(new XElement("Quarters", new XAttribute("Value", QuarterXmlName(quarter))));
        }
    }

    private static string PathXmlName(DynamicPathMode mode) => mode == DynamicPathMode.Bezier ? "Bezier" : "Sin";
    private static string QuarterXmlName(DynamicSinQuarter quarter) => quarter switch
    {
        DynamicSinQuarter.EaseIn => "1QuarterAcc",
        DynamicSinQuarter.EaseOut => "1QuarterDec",
        DynamicSinQuarter.EaseInOut => "2Quarters",
        DynamicSinQuarter.FastSlowFast => "2QuartersFastSlowFast",
        _ => "3Quarters"
    };

    private static string RotationXmlName(DynamicRotationMode mode) => mode switch
    {
        DynamicRotationMode.Linear => "Linear",
        DynamicRotationMode.CubicEaseIn => "EaseIn",
        DynamicRotationMode.CubicEaseOut => "EaseOut",
        DynamicRotationMode.SmoothEaseIn => "Sin_1QuarterAcc",
        DynamicRotationMode.SmoothEaseOut => "Sin_1QuarterDec",
        DynamicRotationMode.SmoothEaseInOut => "Sin_2Quarters",
        DynamicRotationMode.FastSlowFast => "Sin_2QuartersFastSlowFast",
        _ => "Sin_3Quarters"
    };

    private static double PathProgress(double value, DynamicPathMode mode, DynamicSinQuarter quarter)
        => mode == DynamicPathMode.Bezier ? Math.Clamp(value, 0, 1) : SinProgress(value, quarter);

    private static double RotationProgress(double value, DynamicRotationMode mode) => mode switch
    {
        DynamicRotationMode.Linear => value,
        DynamicRotationMode.CubicEaseIn => value * value * value,
        DynamicRotationMode.CubicEaseOut => Math.Pow(value - 1, 3) + 1,
        DynamicRotationMode.SmoothEaseIn => SinProgress(value, DynamicSinQuarter.EaseIn),
        DynamicRotationMode.SmoothEaseOut => SinProgress(value, DynamicSinQuarter.EaseOut),
        DynamicRotationMode.SmoothEaseInOut => SinProgress(value, DynamicSinQuarter.EaseInOut),
        DynamicRotationMode.FastSlowFast => SinProgress(value, DynamicSinQuarter.FastSlowFast),
        _ => SinProgress(value, DynamicSinQuarter.BacktrackThenFinish)
    };

    private static double SinProgress(double value, DynamicSinQuarter quarter)
    {
        var t = Math.Clamp(value, 0, 1);
        return quarter switch
        {
            DynamicSinQuarter.EaseIn => 1 - Math.Cos(t * Math.PI / 2),
            DynamicSinQuarter.EaseOut => Math.Sin(t * Math.PI / 2),
            DynamicSinQuarter.EaseInOut => (1 - Math.Cos(t * Math.PI)) / 2,
            DynamicSinQuarter.FastSlowFast => t < 0.5
                ? 0.5 * Math.Sin(t * Math.PI)
                : 0.5 + 0.5 * (1 - Math.Cos((t - 0.5) * Math.PI)),
            _ when t < 1.0 / 3.0 => -Math.Sin(t * 3 * Math.PI / 2),
            _ when t < 2.0 / 3.0 => -1 + (1 - Math.Cos((t - 1.0 / 3.0) * 3 * Math.PI / 2)),
            _ => Math.Sin((t - 2.0 / 3.0) * 3 * Math.PI / 2)
        };
    }

    private static int Lerp(int start, int finish, double amount)
        => (int)Math.Round(start + ((finish - start) * amount));

    private static double Lerp(double start, double finish, double amount)
        => start + ((finish - start) * amount);

    private static string Pretty(double value)
        => Math.Abs(value % 1) < 0.001
            ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
}
