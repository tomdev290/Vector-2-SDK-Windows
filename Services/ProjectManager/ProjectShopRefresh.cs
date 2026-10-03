using System.Diagnostics;

namespace Vector2LevelEditor.Services.ProjectManager;

public static class ProjectShopRefresh
{
    public static int Request(string dataFolder, IEnumerable<string> installedFiles)
    {
        var executable = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFolder))!, "Vector 2.exe");
        var active = false;
        foreach (var process in Process.GetProcessesByName("Vector 2"))
        {
            using (process)
                try { active |= string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase); }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        if (!active) return 0;
        var root = Vector2GameIntegrationLocator.ModDataRootFor(dataFolder);
        return TouchManifests(root, installedFiles);
    }

    public static int RefreshInstalledOffers(string dataFolder)
    {
        var root = Vector2GameIntegrationLocator.ModDataRootFor(dataFolder);
        var folder = Path.Combine(root, "custom_tricks");
        var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "trick.xml", new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Select(path => Path.GetRelativePath(root, path)).ToArray() : [];
        return Request(dataFolder, files);
    }

    public static int TouchManifests(string root, IEnumerable<string> installedFiles)
    {
        var touched = 0;
        // Request the existing content watcher; this does not verify visible offers.
        foreach (var relative in installedFiles.Where(path => Path.GetFileName(path).Equals("trick.xml", StringComparison.OrdinalIgnoreCase)))
        {
            var path = ProjectManifestService.SafeCombine(root, relative);
            if (!File.Exists(path)) continue;
            var manifest = XmlDraftParsing.Parse(File.ReadAllText(path)).Root;
            var enabled = (string?)manifest?.Attribute("ShopEnabled");
            if (manifest?.Name != "CustomTrick" || enabled == "0" || string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase)) continue;
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            touched++;
        }
        return touched;
    }
}
