using Vector2LevelEditor.Services;

var catalog = new Vector2Catalog();
var assets = catalog.RefreshSnapshot();
var textures = assets.Count(asset => asset.Kind.ToString() == "Texture");
var libraryObjects = assets.Count - textures;
if (assets.Count < 2500 || textures == 0 || libraryObjects == 0)
{
    throw new InvalidOperationException($"Incomplete asset catalog: {assets.Count} total, {textures} textures, {libraryObjects} objects.");
}

Console.WriteLine($"Asset catalog smoke test passed: {assets.Count} total, {textures} textures, {libraryObjects} objects.");
