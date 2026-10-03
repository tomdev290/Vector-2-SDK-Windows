using System.Xml;
using System.Xml.Linq;

namespace Vector2LevelEditor.Services;

public static class XmlDraftParsing
{
    public static string Serialize(XDocument document) => document.Declaration is { } declaration
        ? declaration + "\n" + document.ToString() : document.ToString();

    public static XDocument Parse(string source, LoadOptions options = LoadOptions.None)
    {
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings
        {
            XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit, MaxCharactersFromEntities = 1024
        });
        return XDocument.Load(reader, options);
    }
}
