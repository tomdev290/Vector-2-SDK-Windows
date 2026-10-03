using System.Windows.Controls;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public static class ProjectStudioRouteFactory
{
    public static IReadOnlySet<string> SupportedSections { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Content", "Chapters", "Zones", "Story", "Trigger Designer", "Protocols", "Generator",
        "Quests", "Localization", "Models", "Traps", "Obstacles", "Tricks", "Upgrades", "Shop",
        "Missions", "Rewards", "Tutorials", "Audio", "Assets", "Backgrounds", "Save Data"
    };

    public static UserControl Create(string section, string root, string? gameData, Action<string> openRoom, Action<string> status, Action<string>? navigate = null) => section switch
    {
        "Content" => new ProjectMediaStudioView(root, section, "custom_rooms", openRoom, status, navigate),
        "Assets" => new ProjectMediaStudioView(root, section, "custom_textures", null, status),
        "Backgrounds" => new ProjectMediaStudioView(root, section, "custom_backgrounds", null, status),
        "Chapters" or "Zones" or "Localization" => new StructuredStudioView(root, section, status),
        "Quests" => new QuestStudioView(root, status),
        "Story" => new StoryStudioView(root, status),
        "Tricks" => new TrickLibraryStudioView(root, status, navigate),
        "Obstacles" => new ObstacleLibraryStudioView(root, status),
        "Shop" => new CommerceStudioView(root, true, status),
        "Upgrades" => new CommerceStudioView(root, false, status),
        "Trigger Designer" => new TriggerStudioView(root, status),
        "Protocols" => new ProtocolStudioView(root, status),
        "Generator" => new GeneratorStudioView(root, status),
        "Missions" => new SdkDefinitionStudioView(root, SdkDefinitionKind.Mission, status),
        "Rewards" => new SdkDefinitionStudioView(root, SdkDefinitionKind.Reward, status),
        "Tutorials" => new SdkDefinitionStudioView(root, SdkDefinitionKind.Tutorial, status),
        "Audio" => new AudioStudioView(root, status),
        "Save Data" => new SaveProfileStudioView(root, gameData, status),
        "Models" => new ModelStudioView(root, status),
        "Traps" => new TrapStudioView(root, status),
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "No dedicated Project Manager studio is registered.")
    };
}
