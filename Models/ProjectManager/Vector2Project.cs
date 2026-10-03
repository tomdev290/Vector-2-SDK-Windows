using System.Collections.ObjectModel;

namespace Vector2LevelEditor.Models.ProjectManager;

public sealed class Vector2Project : NotifyObject
{
    private string _name = "Untitled Project";
    private string _author = "";
    private string _version = "1.0.0";
    private string _description = "";
    private string _coverPath = "";
    private string _outputFolder = "";
    private string _gameSourcePath = "";
    private string _gameDataPath = "";
    private bool _generateDebugData;
    public string RootPath { get; init; } = "";
    public string ProjectId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Author { get => _author; set => Set(ref _author, value); }
    public string Version { get => _version; set => Set(ref _version, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string CoverPath { get => _coverPath; set => Set(ref _coverPath, value); }
    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value); }
    public string GameSourcePath { get => _gameSourcePath; set => Set(ref _gameSourcePath, value); }
    public string GameDataPath { get => _gameDataPath; set => Set(ref _gameDataPath, value); }
    public bool GenerateDebugData { get => _generateDebugData; set => Set(ref _generateDebugData, value); }
    public ObservableCollection<ProjectZoneArtwork> ZoneArtwork { get; } = [];
}

public sealed record ProjectZoneArtwork(string Folder, string Path);

public sealed class ProjectFileItem
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required string Section { get; init; }
    public required long Size { get; init; }
    public required DateTime Modified { get; init; }
    public string SizeText => Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024d:0.#} KB" : $"{Size / 1048576d:0.#} MB";
}

public sealed record ProjectValidationIssue(string Severity, string Source, string Message);
