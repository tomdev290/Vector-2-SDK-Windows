using System.IO;
using System.Text;

namespace Vector2LevelEditor.Services;

public sealed record Vector2GameIntegration(string Executable, string DataFolder, string ModDataRoot)
{
    public string CustomRooms => Path.Combine(ModDataRoot, "custom_rooms");
}

public static class Vector2GameIntegrationLocator
{
    public static Vector2GameIntegration? Detect(string? applicationDirectory = null)
    {
        var executable = Vector2PlayBridge.ResolveGameExecutable(null, applicationDirectory);
        if (executable.Length == 0) return null;
        var dataFolder = Path.Combine(Path.GetDirectoryName(executable)!,
            Path.GetFileNameWithoutExtension(executable) + "_Data");
        if (!Directory.Exists(dataFolder) ||
            !Directory.Exists(Path.Combine(dataFolder, "il2cpp_data")) &&
            !Directory.Exists(Path.Combine(dataFolder, "Managed")))
            return null;
        return new Vector2GameIntegration(executable, dataFolder, ModDataRootFor(dataFolder));
    }

    public static string ModDataRootFor(string dataFolder)
    {
        // The supplied IL2CPP player uses this root; newer source-only players use LocalLow.
        foreach (var metadata in new[] {
            Path.Combine(dataFolder, "il2cpp_data", "Metadata", "global-metadata.dat"),
            Path.Combine(dataFolder, "Managed", "Assembly-CSharp.dll") })
            if (File.Exists(metadata) && File.ReadAllBytes(metadata).AsSpan().IndexOf("Vector2ModData"u8) >= 0)
                return Path.Combine(dataFolder, "StreamingAssets", "Vector2ModData");
        return CustomContentService.DefaultStorageRoot;
    }
}
