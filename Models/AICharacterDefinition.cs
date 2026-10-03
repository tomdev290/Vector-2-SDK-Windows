using System.Collections.ObjectModel;

namespace Vector2LevelEditor.Models;

public sealed class AICharacterDefinition : NotifyObject
{
    private string _name = "";
    private string _kind = "Friendly";
    private int _aiChannel = 1;
    private string _bodySkin = "1.xml";
    private string _chestSkin = "";
    private string _helmetSkin = "";
    private string _hairSkin = "";
    private string _birthSpawn = "DefaultSpawn";
    private double _startDelay;
    private bool _spawnOnStart = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Kind { get => _kind; set => Set(ref _kind, value); }
    public int AIChannel { get => _aiChannel; set => Set(ref _aiChannel, value); }
    public string BodySkin { get => _bodySkin; set => Set(ref _bodySkin, value); }
    public string ChestSkin { get => _chestSkin; set => Set(ref _chestSkin, value); }
    public string HelmetSkin { get => _helmetSkin; set => Set(ref _helmetSkin, value); }
    public string HairSkin { get => _hairSkin; set => Set(ref _hairSkin, value); }
    public string BirthSpawn { get => _birthSpawn; set => Set(ref _birthSpawn, value); }
    public double StartDelay { get => _startDelay; set => Set(ref _startDelay, value); }
    public bool SpawnOnStart { get => _spawnOnStart; set => Set(ref _spawnOnStart, value); }
    public ObservableCollection<string> CustomLayers { get; } = [];
    public IEnumerable<string> SkinFiles => new[] { BodySkin, ChestSkin, HelmetSkin, HairSkin }
        .Concat(CustomLayers).Select(name => name.Trim()).Where(name => name.Length > 0);
}

public sealed class AIGroupDefinition : NotifyObject
{
    private string _name = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<Guid> CharacterIds { get; } = [];
}
