namespace Vector2LevelEditor.Services.ProjectManager;

public static class ProjectPackageService
{
    public static void DeletePackage(string libraryFolder, string manifest)
    {
        var library = Path.GetFullPath(libraryFolder);
        var package = Path.GetFullPath(Path.GetDirectoryName(manifest) ?? "");
        if (!string.Equals(Path.GetDirectoryName(package), library, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected manifest is not inside a project package.");
        Directory.Delete(package, true);
    }
}
