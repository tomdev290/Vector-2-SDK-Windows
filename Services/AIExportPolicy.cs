using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class AIExportPolicy
{
    public static bool SpawnOnStart(LevelDocument document, AICharacterDefinition character)
    {
        if (!character.SpawnOnStart) return false;
        return !document.SceneNodes.Any(node => node.Kind == LevelNodeKind.Trigger && node.TriggerAction == "Spawn" &&
            (node.TriggerTarget == "all" || node.TriggerTarget == "character:" + character.Id ||
             node.TriggerTarget.StartsWith("group:", StringComparison.Ordinal) &&
             Guid.TryParse(node.TriggerTarget[6..], out var groupId) &&
             document.AIGroups.Any(group => group.Id == groupId && group.CharacterIds.Contains(character.Id))));
    }
}
