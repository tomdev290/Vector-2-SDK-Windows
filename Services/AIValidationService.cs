using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class AIValidationService
{
    public static IReadOnlyList<string> Validate(LevelDocument document)
    {
        var issues = new List<string>();
        var characters = document.AICharacters;
        if (characters.Any(character => string.IsNullOrWhiteSpace(character.Name)))
            issues.Add("Every AI needs a name.");
        if (characters.GroupBy(character => character.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            issues.Add("AI names must be unique.");
        if (characters.Any(character => character.AIChannel < 1 || character.AIChannel > 999) ||
            characters.GroupBy(character => character.AIChannel).Any(group => group.Count() > 1))
            issues.Add("AI channels must be unique values from 1 to 999.");
        if (characters.Any(character => string.IsNullOrWhiteSpace(character.BodySkin)))
            issues.Add("Every AI needs a body model.");
        if (characters.Any(character => character.StartDelay < 0 || character.StartDelay > 30))
            issues.Add("AI start delay must be between 0 and 30 seconds.");
        var ids = characters.Select(character => character.Id).ToHashSet();
        if (document.AIGroups.SelectMany(group => group.CharacterIds).Any(id => !ids.Contains(id)))
            issues.Add("An AI group contains a deleted character.");
        return issues;
    }
}
