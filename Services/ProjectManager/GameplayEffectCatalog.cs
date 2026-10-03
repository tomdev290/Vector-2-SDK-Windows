namespace Vector2LevelEditor.Services.ProjectManager;

public sealed record GameplayEffect(string Id, string Name, string Description, IReadOnlyDictionary<string, string> Parameters)
{
    public override string ToString() => Name;
}

public static class GameplayEffectCatalog
{
    public static IReadOnlyList<GameplayEffect> CardEffects { get; } =
    [
        new("None", "Collectible only", "The card can be collected but does not alter gameplay.", new Dictionary<string, string>()),
        new("RestoreChargesOnFloorStart", "Restore charges each floor", "Restores charges when gameplay for a floor begins.", new Dictionary<string, string> { ["Amount"] = "2" }),
        new("FillChargesOnFloorStart", "Refill gadgets each floor", "Fills equipped gadgets to their normal capacity when a floor begins.", new Dictionary<string, string>())
    ];

    public static GameplayEffect? Find(string id) => CardEffects.FirstOrDefault(effect => effect.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static void ConfigureLevels(System.Xml.Linq.XElement card, string effectId)
    {
        var effect = Find(effectId);
        if (effect is null) return;
        foreach (var level in card.Elements("Level"))
        {
            foreach (var attribute in level.Attributes().Where(attribute => attribute.Name.LocalName is "Amount" or "Interval" or "Maximum").ToList())
                attribute.Remove();
            foreach (var parameter in effect.Parameters) level.SetAttributeValue(parameter.Key, parameter.Value);
        }
    }
}
