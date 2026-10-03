using System.Xml.Linq;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Services;

public sealed record XmlAttributeSuggestion(int Start, int Length, string Prefix, IReadOnlyList<string> Choices);

public static class XmlAttributeCompletion
{
    public static XmlAttributeSuggestion? AtCaret(string source, int caret)
    {
        if (caret < 0 || caret > source.Length) return null;
        var opening = -1;
        char quote = '\0';
        for (var index = 0; index < caret; index++)
        {
            if (opening < 0 && (source.AsSpan(index).StartsWith("<!--") || source.AsSpan(index).StartsWith("<![CDATA[")))
            {
                var ending = source.AsSpan(index).StartsWith("<!--") ? "-->" : "]]>";
                var end = source.IndexOf(ending, index + 4, StringComparison.Ordinal);
                if (end < 0 || end + ending.Length >= caret) return null;
                index = end + ending.Length - 1;
            }
            else if (quote != '\0') { if (source[index] == quote) quote = '\0'; }
            else if (opening >= 0 && source[index] is '\'' or '"') quote = source[index];
            else if (source[index] == '<') opening = index;
            else if (source[index] == '>') opening = -1;
        }
        if (opening < 0 || opening + 1 >= source.Length || source[opening + 1] is '/' or '!' or '?') return null;
        var position = opening + 1;
        while (position < source.Length && NameChar(source[position])) position++;
        var tag = source[(opening + 1)..position];
        if (caret <= position) return null;
        var schema = TriggerRuntimeSchema.Actions.Concat(TriggerRuntimeSchema.Conditions).Concat(TriggerRuntimeSchema.Events).FirstOrDefault(item => item.Name == tag);
        var attributes = (schema?.Required ?? []).Concat(schema?.Defaults.Keys ?? []).Distinct().ToArray();
        if (tag == "Trigger") attributes = ["Name", "X", "Y", "Width", "Height"];
        if (tag == "SetVariable") attributes = ["Name", "Type", "Value"];
        var allAttributes = AttributeNames(source, position).ToArray();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (position < source.Length)
        {
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            var start = position;
            while (position < source.Length && NameChar(source[position])) position++;
            if (caret >= start && caret <= position)
                return new(start, position - start, source[start..caret], attributes.Where(name => !allAttributes.Any(item => item.Start != start && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray());
            if (position == start) return null;
            var attribute = source[start..position]; present.Add(attribute);
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            if (position >= source.Length || source[position++] != '=') return null;
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            if (position >= source.Length || source[position] is not ('\'' or '"')) return null;
            var delimiter = source[position++]; var valueStart = position;
            while (position < source.Length && source[position] != delimiter) position++;
            if (caret >= valueStart && caret <= position)
            {
                var values = new List<string>();
                if (schema?.Defaults.TryGetValue(attribute, out var value) == true) values.Add(value);
                if (tag == "ForceAnimation" && attribute == "Name") values.AddRange(XmlAnimationSuggestions.Names());
                if (attribute == "Model")
                {
                    values.Add("Player");
                    try
                    {
                        var document = XDocument.Parse(source, LoadOptions.SetLineInfo);
                        var line = 1 + source.AsSpan(0, opening).Count('\n');
                        var lineStart = source.LastIndexOf('\n', Math.Max(0, opening - 1)) + 1;
                        var column = opening - lineStart + 1;
                        var current = document.Descendants().FirstOrDefault(element => element is System.Xml.IXmlLineInfo info && info.LineNumber == line && Math.Abs(info.LinePosition - column) <= 1);
                        var trigger = current?.AncestorsAndSelf("Trigger").FirstOrDefault();
                        values.AddRange((trigger?.Element("Content")?.Element("Init") ?? trigger?.Element("Init"))?.Elements("SetVariable")
                            .Where(node => (string?)node.Attribute("Type") == "AI")
                            .Select(node => (string?)node.Attribute("Name")).OfType<string>() ?? []);
                    }
                    catch (System.Xml.XmlException) { }
                }
                return new(valueStart, position - valueStart, source[valueStart..caret], values.Distinct().ToArray());
            }
            if (position >= source.Length) return null;
            position++;
        }
        return null;
    }
    private static bool NameChar(char value) => char.IsLetterOrDigit(value) || value is '_' or ':' or '-' or '.';
    private static IEnumerable<(int Start, string Name)> AttributeNames(string source, int position)
    {
        while (position < source.Length)
        {
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            var start = position;
            while (position < source.Length && NameChar(source[position])) position++;
            if (position == start) yield break;
            yield return (start, source[start..position]);
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            if (position >= source.Length || source[position++] != '=') yield break;
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            if (position >= source.Length || source[position] is not ('\'' or '"')) yield break;
            var delimiter = source[position++];
            while (position < source.Length && source[position] != delimiter) position++;
            if (position < source.Length) position++;
        }
    }
}
