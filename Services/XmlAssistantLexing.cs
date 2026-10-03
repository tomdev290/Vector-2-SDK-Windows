namespace Vector2LevelEditor.Services;

public readonly record struct XmlNameToken(int MarkupStart, int Start, int Length, bool Closing);

public static class XmlAssistantLexing
{
    private static bool NameChar(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or ':' or '-' or '.';

    // .NET string indices are UTF-16, matching WPF selection offsets.
    public static XmlNameToken? NameAtCaret(string source, int caret)
    {
        if (caret < 0 || caret > source.Length) return null;
        var markup = -1;
        char quote = '\0';
        for (var i = 0; i < caret; i++)
        {
            if (markup < 0)
            {
                if (source.AsSpan(i).StartsWith("<!--"))
                {
                    var end = source.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    if (end < 0 || caret <= end + 3) return null;
                    i = end + 2;
                }
                else if (source.AsSpan(i).StartsWith("<![CDATA["))
                {
                    var end = source.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                    if (end < 0 || caret <= end + 3) return null;
                    i = end + 2;
                }
                else if (source[i] == '<') markup = i;
            }
            else if (quote != '\0')
            {
                if (source[i] == quote) quote = '\0';
            }
            else if (source[i] is '\'' or '"') quote = source[i];
            else if (source[i] == '>') markup = -1;
        }
        if (markup < 0 || quote != '\0') return null;
        var start = markup + 1;
        var closing = start < source.Length && source[start] == '/';
        if (closing) start++;
        var endName = start;
        while (endName < source.Length && NameChar(source[endName])) endName++;
        return caret >= start && caret <= endName
            ? new XmlNameToken(markup, start, endName - start, closing) : null;
    }

    public static bool IsEmptyDraft(string source)
    {
        using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(source),
            new System.Xml.XmlReaderSettings { ConformanceLevel = System.Xml.ConformanceLevel.Fragment,
                DtdProcessing = System.Xml.DtdProcessing.Prohibit });
        while (reader.Read())
            if (reader.NodeType is not (System.Xml.XmlNodeType.Comment or System.Xml.XmlNodeType.Whitespace
                or System.Xml.XmlNodeType.SignificantWhitespace or System.Xml.XmlNodeType.XmlDeclaration)) return false;
        return true;
    }
}
