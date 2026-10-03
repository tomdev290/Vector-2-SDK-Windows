namespace Vector2LevelEditor.Services;

public static class XmlAnimationSuggestions
{
    private static readonly object Gate = new();
    private static DateTime _expires;
    private static string _root = "";
    private static IReadOnlyList<string> _names = [];

    public static IReadOnlyList<string> Names()
    {
        lock (Gate)
        {
            var root = StructuralRoomService.CurrentProjectRoot();
            if (_root == root && DateTime.UtcNow < _expires) return _names;
            try { _names = new GameTrickPreviewService().LoadMoves().Select(move => move.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray(); }
            catch (Exception error) when (error is IOException or System.Xml.XmlException or InvalidDataException)
            { Diagnostics.DiagnosticsLog.Warn("Animation suggestions unavailable: " + error.Message); _names = []; }
            _root = root; _expires = DateTime.UtcNow.AddSeconds(2);
            return _names;
        }
    }
}
