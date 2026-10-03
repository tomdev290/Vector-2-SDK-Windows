namespace Vector2LevelEditor.Models.ProjectManager;

public sealed record ProjectSection(string Name, string Folder, string Icon, string Description)
{
    public static bool ShopEditingAvailable => false;
    public string DisplayName => Name == "Story" ? "Story & Dialogue" : Name;
    public static IReadOnlyList<ProjectSection> All { get; } =
    [
        new("Overview", "", "\uE80F", "Project health, recent content, and installation"),
        new("Content", "custom_rooms", "\uE8B7", "Rooms and structural room XML"),
        new("Chapters", "custom_chapters", "\uE82D", "Chapter definitions and progression"),
        new("Zones", "custom_zones", "\uE81E", "Zone definitions and room pools"),
        new("Story", "custom_story", "\uE8F2", "Story graphs, dialogue, and cast"),
        new("Trigger Designer", "custom_triggers", "\uE7C8", "Reusable trigger definitions"),
        new("Protocols", "custom_protocols", "\uEA18", "Player protocols, armor, and rules"),
        new("Generator", "custom_gamedata\\generator_data", "\uE950", "Run generator data"),
        new("Quests", "custom_quests", "\uE8A4", "Quest sequences and objectives"),
        new("Localization", "custom_localization", "\uE8C1", "Localized phrases"),
        new("Models", "custom_models", "\uE809", "Custom model packages"),
        new("Obstacles", "custom_obstacles", "\uE7B8", "Reusable obstacle packages"),
        new("Traps", "custom_traps", "\uE7BA", "Custom trap definitions"),
        new("Tricks", "custom_tricks", "\uE7FC", "Tricks and animation overrides"),
        new("Upgrades", "custom_upgrades", "\uE74A", "Upgrade definitions"),
        new("Shop", "custom_upgrades", "\uE7BF", "Shop cards and economy content"),
        new("Missions", "custom_missions", "\uE9D9", "Mission definitions"),
        new("Rewards", "custom_rewards", "\uF133", "Reward definitions"),
        new("Tutorials", "custom_tutorials", "\uE82F", "Tutorial sequences"),
        new("Audio", "custom_audio", "\uE8D6", "Music, ambience, and sound pools"),
        new("Assets", "custom_textures", "\uEB9F", "Custom textures and artwork"),
        new("Backgrounds", "custom_backgrounds", "\uE91B", "Background library"),
        new("Save Data", "custom_save_data", "\uE8F7", "Save profile definitions")
    ];
    public static ProjectSection Overview => All[0];
}
