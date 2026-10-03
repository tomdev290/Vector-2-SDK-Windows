using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Vector2LevelEditor.Services;

public sealed record XmlDraftSolution(string Title, string Code, bool Recommended)
{
    public string Explanation { get; init; } = "Review this local, rule-based proposal before applying.";
    public int? RemainingIssues { get; init; }
}
public sealed record XmlDraftIntent(int ElementIndex, string Tag, IReadOnlyList<string> MissingFields);
public sealed record XmlDraftReview(string Source, IReadOnlyList<XmlDraftSolution> Solutions)
{
    public IReadOnlyList<XmlDraftIntent> Intents { get; init; } = [];
}

public static class XmlDraftReviewer
{
    public static XmlDraftReview Review(string source)
    {
        if (source.Length > 200_000) return new(source, []);
        var escaped = EscapeLiteralAmpersands(source);
        var solutions = new List<XmlDraftSolution>();
        var intents = new List<XmlDraftIntent>();
        if (escaped != source && Parses(escaped))
            solutions.Add(new("Escape literal ampersands", escaped, true));
        AddDuplicateRepairs(source, solutions);
        AddRuntimeRepairs(source, solutions, intents);
        if (!Parses(escaped)) solutions.AddRange(XmlStructuralRepairs.Proposals(source));
        int? originalErrors = null;
        try { originalErrors = XmlDraftParsing.Parse(source).Descendants("Trigger").Sum(trigger => ProjectManager.TriggerRuntimeSchema.Diagnose(trigger).Count(issue => issue.IsError)); }
        catch (XmlException) { }
        var checkedSolutions = solutions.DistinctBy(solution => solution.Code).Take(32).Select(solution => CheckCandidate(solution, originalErrors)).ToArray();
        if (checkedSolutions.Count(solution => solution.Recommended) > 1)
            checkedSolutions = checkedSolutions.Select(solution => solution with { Recommended = false }).ToArray();
        return new(source, checkedSolutions) { Intents = intents };
    }

    private static XmlDraftSolution CheckCandidate(XmlDraftSolution solution, int? originalErrors = null)
    {
        var document = XmlDraftParsing.Parse(solution.Code);
        var triggers = document.Descendants("Trigger").ToArray();
        var errors = triggers.Sum(trigger => ProjectManager.TriggerRuntimeSchema.Diagnose(trigger).Count(issue => issue.IsError));
        return solution with { Recommended = solution.Recommended && (errors == 0 || originalErrors is int before && errors < before), RemainingIssues = triggers.Length > 0 ? errors : null };
    }

    public static XmlDraftSolution CompleteIntent(string source, XmlDraftIntent intent, IReadOnlyDictionary<string, string> values)
    {
        if (source.Length > 200_000) throw new InvalidDataException("This draft exceeds the detailed repair limit.");
        if (intent.MissingFields.Any(key => !values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException("Complete every required field before previewing.");
        var document = XmlDraftParsing.Parse(source);
        var element = document.Descendants().ElementAt(intent.ElementIndex);
        if (element.Name.LocalName != intent.Tag) throw new InvalidDataException("The XML element changed. Recheck the draft.");
        foreach (var key in intent.MissingFields) element.SetAttributeValue(key, values[key]);
        return CheckCandidate(new("Complete " + intent.Tag + " fields", XmlDraftParsing.Serialize(document), false)
        {
            Explanation = "Uses your explicit values. Review the diff and run Apply XML validation; no gameplay values were inferred."
        });
    }

    private static void AddRuntimeRepairs(string source, List<XmlDraftSolution> solutions, List<XmlDraftIntent> intents)
    {
        XDocument document;
        try { document = XmlDraftParsing.Parse(source); }
        catch (XmlException) { return; }
        var elements = document.Descendants().ToList();
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var projectFields = XmlAssistantContext.ProjectFieldsNeedingInput(element);
            if (projectFields.Length > 0) intents.Add(new(index, element.Name.LocalName, projectFields));
            var catalogue = element.Parent?.Name.LocalName switch
            {
                "Events" => ProjectManager.TriggerRuntimeSchema.Events,
                "Conditions" => ProjectManager.TriggerRuntimeSchema.Conditions,
                "Actions" => ProjectManager.TriggerRuntimeSchema.Actions,
                _ => null
            };
            if (catalogue is null || element.Name.NamespaceName.Length != 0) continue;
            var item = catalogue.FirstOrDefault(candidate => candidate.Name.Equals(element.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                var nearby = catalogue.Select(candidate => (Item: candidate, Distance: XmlStructuralRepairs.Distance(element.Name.LocalName, candidate.Name)))
                    .Where(candidate => candidate.Distance <= 2).OrderBy(candidate => candidate.Distance).ToArray();
                if (nearby.Length > 0 && nearby.Count(candidate => candidate.Distance == nearby[0].Distance) == 1) item = nearby[0].Item;
            }
            if (item is null) continue;
            var requiredFields = XmlAssistantContext.RequiredFields(element, item);
            var missing = requiredFields.Where(key => string.IsNullOrWhiteSpace((string?)element.Attribute(key))).ToArray();
            if (missing.Length > 0) intents.Add(new(index, element.Name.LocalName, missing));
            var elementProposalCount = 0;
            void Propose(string title, bool recommended, Action<XElement> edit)
            {
                if (solutions.Count >= 32 || elementProposalCount >= 3) return;
                var copy = new XDocument(document);
                edit(copy.Descendants().ElementAt(index));
                solutions.Add(new(title, XmlDraftParsing.Serialize(copy), recommended));
                elementProposalCount++;
            }
            if (element.Name.LocalName != item.Name)
                Propose("Correct supported tag spelling to " + item.Name, true, node => node.Name = item.Name);
            foreach (var key in requiredFields)
            {
                var attribute = element.Attribute(key);
                if (attribute is not null && !string.IsNullOrWhiteSpace(attribute.Value)) continue;
                var wrongCase = element.Attributes().FirstOrDefault(attr => attr.Name.LocalName.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (wrongCase is not null && wrongCase.Name.LocalName != key)
                {
                    var name = wrongCase.Name; var value = wrongCase.Value;
                    Propose("Correct attribute capitalization to " + key, true, node => { node.Attribute(name)?.Remove(); node.SetAttributeValue(key, value); });
                    continue;
                }
                IEnumerable<string> values = item.Defaults.TryGetValue(key, out var defaultValue) ? [defaultValue] : [];
                if (item.Name == "ForceAnimation" && key == "Name") values = XmlAnimationSuggestions.Names().Take(3);
                if (key == "Model")
                {
                    var trigger = element.Ancestors("Trigger").FirstOrDefault();
                    var variables = trigger?.Element("Content")?.Element("Init")?.Elements("SetVariable")
                        .Where(node => (string?)node.Attribute("Type") == "AI")
                        .Select(node => (string?)node.Attribute("Name")).OfType<string>().Where(name => name.Length > 0) ?? [];
                    values = new[] { "Player" }.Concat(variables).Distinct(StringComparer.Ordinal);
                }
                if (key is "Value1" or "Value2" && item.Name is "Equal" or "Greater" or "Less" or "GreaterEqual" or "LessEqual")
                {
                    var other = (string?)element.Attribute(key == "Value1" ? "Value2" : "Value1") ?? "";
                    var trigger = element.Ancestors("Trigger").FirstOrDefault();
                    var declarations = (trigger?.Element("Content")?.Element("Init") ?? trigger?.Element("Init"))?.Elements("SetVariable") ?? [];
                    var numeric = declarations.GroupBy(node => (string?)node.Attribute("Name"), StringComparer.Ordinal)
                        .Where(group => group.Key is { Length: > 0 } && group.Count() == 1).Select(group => group.Single())
                        .Where(IsNumericVariable).ToArray();
                    var candidates = new List<string>();
                    var counterpart = numeric.FirstOrDefault(node => "_" + (string?)node.Attribute("Name") == other);
                    if (counterpart is not null)
                    {
                        candidates.Add((string)counterpart.Attribute("Value")!);
                        if ((string?)counterpart.Attribute("Type") == "Bool") candidates.AddRange(["0", "1"]);
                    }
                    candidates.AddRange(numeric.Select(node => "_" + (string?)node.Attribute("Name")).Where(value => value != other));
                    values = candidates.Distinct(StringComparer.Ordinal);
                }
                foreach (var value in values)
                    Propose($"Set {key} to {value}", key is not ("Model" or "Value1" or "Value2"), node => node.SetAttributeValue(key, value));
            }
        }
    }

    private static bool IsNumericVariable(XElement node)
    {
        if ((string?)node.Attribute("Type") is "AI" or "Node") return false;
        var value = (string?)node.Attribute("Value") ?? "";
        if (value.Length == 0 || !"+-0123456789".Contains(value[0])) return false;
        return value.Contains('.')
            ? float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && float.IsFinite(number)
            : int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private sealed record AttributeToken(string Name, string Value, int Start, int End);

    private static void AddDuplicateRepairs(string source, List<XmlDraftSolution> solutions)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '<') continue;
            if (source.AsSpan(i).StartsWith("<!--") || source.AsSpan(i).StartsWith("<![CDATA["))
            {
                var ending = source.AsSpan(i).StartsWith("<!--") ? "-->" : "]]>";
                var end = source.IndexOf(ending, i + 4, StringComparison.Ordinal);
                if (end < 0) return;
                i = end + ending.Length - 1; continue;
            }
            if (i + 1 == source.Length || source[i + 1] is '/' or '!' or '?') continue;
            var cursor = i + 1;
            while (cursor < source.Length && !char.IsWhiteSpace(source[cursor]) && source[cursor] is not ('/' or '>')) cursor++;
            var attributes = new List<AttributeToken>();
            while (cursor < source.Length)
            {
                var start = cursor;
                while (cursor < source.Length && char.IsWhiteSpace(source[cursor])) cursor++;
                if (cursor == source.Length || source[cursor] is '/' or '>') break;
                var nameStart = cursor;
                while (cursor < source.Length && !char.IsWhiteSpace(source[cursor]) && source[cursor] is not ('=' or '>' or '/')) cursor++;
                var name = source[nameStart..cursor];
                while (cursor < source.Length && char.IsWhiteSpace(source[cursor])) cursor++;
                if (cursor == source.Length || source[cursor++] != '=') break;
                while (cursor < source.Length && char.IsWhiteSpace(source[cursor])) cursor++;
                if (cursor == source.Length || source[cursor] is not ('\'' or '"')) break;
                var quote = source[cursor++]; var valueStart = cursor;
                while (cursor < source.Length && source[cursor] != quote) cursor++;
                if (cursor == source.Length) break;
                var value = source[valueStart..cursor++];
                attributes.Add(new(name, value, start, cursor));
            }
            foreach (var duplicate in attributes.GroupBy(item => item.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                var alternatives = duplicate.GroupBy(item => item.Value, StringComparer.Ordinal).Select(group => group.First()).ToList();
                foreach (var keep in alternatives)
                {
                    var proposal = source;
                    foreach (var remove in duplicate.Where(item => item != keep).OrderByDescending(item => item.Start))
                        proposal = proposal.Remove(remove.Start, remove.End - remove.Start);
                    if (Parses(proposal)) solutions.Add(new($"Keep {duplicate.Key}=\"{keep.Value}\"", proposal, alternatives.Count == 1));
                }
            }
            i = Math.Max(i, cursor - 1);
        }
    }

    private static bool Parses(string source)
    {
        try { return XmlDraftParsing.Parse(source).Root is not null; }
        catch (XmlException) { return false; }
    }

    public static string EscapeLiteralAmpersands(string source)
    {
        var output = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var ending = source.AsSpan(i).StartsWith("<!--") ? "-->"
                : source.AsSpan(i).StartsWith("<![CDATA[") ? "]]>" : null;
            if (ending is not null)
            {
                var end = source.IndexOf(ending, i + 4, StringComparison.Ordinal);
                if (end < 0) { output.Append(source.AsSpan(i)); break; }
                output.Append(source.AsSpan(i, end + ending.Length - i));
                i = end + ending.Length - 1;
                continue;
            }
            if (source[i] != '&') { output.Append(source[i]); continue; }
            var semicolon = source.IndexOf(';', i + 1);
            var entity = semicolon > i && semicolon - i < 32 ? source[(i + 1)..semicolon] : "";
            var valid = entity is "amp" or "lt" or "gt" or "quot" or "apos";
            if (entity.StartsWith('#'))
            {
                var hex = entity.StartsWith("#x", StringComparison.Ordinal);
                var digits = entity[(hex ? 2 : 1)..];
                valid = digits.Length > 0 && digits.All(ch => hex ? Uri.IsHexDigit(ch) : char.IsAsciiDigit(ch));
            }
            output.Append(valid ? "&" : "&amp;");
        }
        return output.ToString();
    }
}

public enum XmlDiffKind { Unchanged, Removed, Added }
public sealed record XmlDiffLine(XmlDiffKind Kind, string Text);

public static class XmlSolutionDiff
{
    public static IReadOnlyList<XmlDiffLine> Lines(string before, string after)
    {
        static string[] Split(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var a = Split(before); var b = Split(after);
        var result = new List<XmlDiffLine>();
        // Bound memory for large drafts; a complete replacement is still an honest, reconstructible diff.
        if ((long)a.Length * b.Length > 1_000_000)
        {
            result.AddRange(a.Select(line => new XmlDiffLine(XmlDiffKind.Removed, line)));
            result.AddRange(b.Select(line => new XmlDiffLine(XmlDiffKind.Added, line)));
            return result;
        }
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lengths[i, j] = a[i] == b[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var x = 0; var y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y]) { result.Add(new(XmlDiffKind.Unchanged, a[x++])); y++; }
            else if (x < a.Length && (y == b.Length || lengths[x + 1, y] >= lengths[x, y + 1])) result.Add(new(XmlDiffKind.Removed, a[x++]));
            else result.Add(new(XmlDiffKind.Added, b[y++]));
        }
        return result;
    }
}
