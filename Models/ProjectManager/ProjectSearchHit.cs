namespace Vector2LevelEditor.Models.ProjectManager;

public sealed record ProjectSearchHit(
    string Title,
    string Detail,
    string Section,
    string Icon,
    ProjectSection? TargetSection,
    ProjectFileItem? File);
