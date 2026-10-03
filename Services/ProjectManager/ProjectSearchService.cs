using System.Globalization;
using Vector2LevelEditor.Models.ProjectManager;

namespace Vector2LevelEditor.Services.ProjectManager;

public static class ProjectSearchService
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Content"] = "rooms levels xml open editor",
        ["Story"] = "characters cast dialogue narrative",
        ["Trigger Designer"] = "trigger xml code events actions variables",
        ["Backgrounds"] = "background parallax zone pool art",
        ["Save Data"] = "player profile armour armor credits",
        ["Assets"] = "textures images imports browser",
        ["Tricks"] = "stunts animation custom trick",
        ["Generator"] = "zones room pool generation"
    };

    public static IReadOnlyList<ProjectSearchHit> Search(
        string query, IEnumerable<ProjectSection> sections, IEnumerable<ProjectFileItem> files)
    {
        query = query.Trim();
        if (query.Length == 0) return [];

        var ranked = new List<(int Score, ProjectSearchHit Hit)>();
        foreach (var section in sections)
        {
            var candidate = section.DisplayName + " " + Aliases.GetValueOrDefault(section.Name, "");
            if (Score(candidate, query) is not { } score) continue;
            ranked.Add((score, new ProjectSearchHit(section.DisplayName, "Open project tool",
                section.DisplayName, section.Icon, section, null)));
        }

        foreach (var file in files.DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            if (Score(file.FullPath, query) is not { } score) continue;
            var section = sections.FirstOrDefault(item => item.Name == file.Section);
            ranked.Add((200 + score, new ProjectSearchHit(Path.GetFileNameWithoutExtension(file.Name),
                file.RelativePath, section?.DisplayName ?? file.Section, section?.Icon ?? "",
                section, file)));
        }

        return ranked.OrderBy(item => item.Score)
            .ThenBy(item => item.Hit.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(14).Select(item => item.Hit).ToList();
    }

    public static int? Score(string candidate, string query)
    {
        var foldedCandidate = Fold(candidate);
        var foldedQuery = Fold(query.Trim());
        if (foldedQuery.Length == 0) return null;
        if (foldedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => !foldedCandidate.Contains(word, StringComparison.Ordinal))) return null;
        if (foldedCandidate == foldedQuery) return 0;
        if (foldedCandidate.StartsWith(foldedQuery, StringComparison.Ordinal))
            return 10 + foldedCandidate.Length;
        var index = foldedCandidate.IndexOf(foldedQuery, StringComparison.Ordinal);
        return 100 + (index < 0 ? 50 : index);
    }

    private static string Fold(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLower(CultureInfo.CurrentCulture);
    }
}
