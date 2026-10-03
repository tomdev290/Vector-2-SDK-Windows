using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Vector2LevelEditor.Services;

public static class XmlStructuralRepairs
{
    private sealed record Tag(int Start, int End, string Name, bool Closing, bool Empty);
    private static readonly Regex BareAttributes = new("\"[^\"]*\"|'[^']*'|(?<name>[A-Za-z_][A-Za-z0-9_:.-]*)\\s*=\\s*(?<value>[^\\s\"'=<>`]+)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public static IEnumerable<XmlDraftSolution> Proposals(string source)
    {
        if (source.Length > 200_000) yield break;
        string normalized;
        try { normalized = NormalizeAttributes(source); }
        catch (RegexMatchTimeoutException) { yield break; }
        normalized = XmlDraftReviewer.EscapeLiteralAmpersands(normalized);
        foreach (var removeEmpty in new[] { true, false })
        {
            var repaired = CloseChain(normalized, removeEmpty, out var removedNonempty);
            if (repaired == source) continue;
            XDocument? parsed = null;
            try { parsed = XmlDraftParsing.Parse(repaired); } catch (XmlException) { }
            if (parsed?.Root is null) continue;
            yield return new XmlDraftSolution(removeEmpty ? "Repair XML nesting and syntax" : "Close unfinished sections",
                repaired, removeEmpty && !removedNonempty) { Explanation = "Preserves existing values and content. Review every change; XML parsing does not prove gameplay intent." };
        }
    }

    private static List<Tag> Tags(string source)
    {
        var tags = new List<Tag>();
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '<') continue;
            var terminator = source.AsSpan(i).StartsWith("<!--") ? "-->" : source.AsSpan(i).StartsWith("<![CDATA[") ? "]]>"
                : source.AsSpan(i).StartsWith("<?") ? "?>" : null;
            if (terminator is not null)
            {
                var endProtected = source.IndexOf(terminator, i + 2, StringComparison.Ordinal);
                if (endProtected < 0) break;
                i = endProtected + terminator.Length - 1; continue;
            }
            var cursor = i + 1;
            var closing = cursor < source.Length && source[cursor] == '/';
            if (closing) cursor++;
            var nameStart = cursor;
            while (cursor < source.Length && (char.IsLetterOrDigit(source[cursor]) || source[cursor] is '_' or ':' or '.' or '-')) cursor++;
            if (cursor == nameStart) continue;
            var name = source[nameStart..cursor]; char quote = '\0';
            for (; cursor < source.Length; cursor++)
            {
                var ch = source[cursor];
                if (quote != '\0') { if (ch == quote) quote = '\0'; }
                else if (ch is '\'' or '"') quote = ch;
                else if (ch == '>') break;
                else if (ch == '<') break;
            }
            if (cursor == source.Length || source[cursor] != '>') break;
            tags.Add(new(i, cursor + 1, name, closing, source[cursor - 1] == '/'));
            i = cursor;
        }
        return tags;
    }

    private static string NormalizeAttributes(string source)
    {
        var builder = new StringBuilder(source);
        foreach (var tag in Tags(source).Where(tag => !tag.Closing).AsEnumerable().Reverse())
        {
            var markup = source[tag.Start..tag.End];
            var replacement = BareAttributes.Replace(markup, match =>
            {
                if (!match.Groups["name"].Success) return match.Value;
                var value = match.Groups["value"].Value;
                var slash = value.EndsWith('/') && match.Index + match.Length == markup.Length - 1;
                if (slash) value = value[..^1];
                return match.Groups["name"].Value + "=\"" + value + "\"" + (slash ? "/" : "");
            });
            builder.Remove(tag.Start, tag.End - tag.Start).Insert(tag.Start, replacement);
        }
        return builder.ToString();
    }

    private static string CloseChain(string source, bool removeEmpty, out bool removedNonempty)
    {
        removedNonempty = false;
        var stack = new List<Tag>(); var output = new StringBuilder(); var position = 0;
        foreach (var tag in Tags(source))
        {
            output.Append(source.AsSpan(position, tag.Start - position));
            if (!tag.Closing)
            {
                output.Append(source.AsSpan(tag.Start, tag.End - tag.Start));
                if (!tag.Empty) stack.Add(tag);
            }
            else
            {
                var match = stack.FindLastIndex(open => open.Name == tag.Name);
                if (match < 0 && stack.Count > 0 && Distance(stack[^1].Name, tag.Name) <= 2) match = stack.Count - 1;
                if (match < 0) removedNonempty = true;
                else
                {
                    while (stack.Count - 1 > match)
                    {
                        var open = stack[^1];
                        var empty = string.IsNullOrWhiteSpace(source[open.End..tag.Start]);
                        var duplicateContent = open.Name == "Content" && tag.Name == "Trigger" &&
                            output.ToString().Contains("</Content>", StringComparison.Ordinal);
                        if (removeEmpty && empty && duplicateContent)
                        {
                            var openingLength = open.End - open.Start;
                            var whitespaceLength = tag.Start - open.End;
                            var removeStart = output.Length - whitespaceLength - openingLength;
                            var lineStart = source.LastIndexOf('\n', Math.Max(0, open.Start - 1)) + 1;
                            if (string.IsNullOrWhiteSpace(source[lineStart..open.Start]) && source[open.End..tag.Start].Contains('\n'))
                            {
                                var newline = source.IndexOf('\n', open.End);
                                output.Remove(removeStart - (open.Start - lineStart), newline + 1 - lineStart);
                            }
                            else output.Remove(removeStart, openingLength);
                        }
                        else output.Append("</").Append(open.Name).Append('>');
                        stack.RemoveAt(stack.Count - 1);
                    }
                    output.Append("</").Append(stack[^1].Name).Append('>');
                    stack.RemoveAt(stack.Count - 1);
                }
            }
            position = tag.End;
        }
        output.Append(source.AsSpan(position));
        for (var i = stack.Count - 1; i >= 0; i--) output.Append("</").Append(stack[i].Name).Append('>');
        return output.ToString();
    }

    public static int Distance(string a, string b)
    {
        if (a.Length > 128 || b.Length > 128 || Math.Abs(a.Length - b.Length) > 2) return 3;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1]; current[0] = i;
            for (var j = 1; j <= b.Length; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                previous[j - 1] + (char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }
}
