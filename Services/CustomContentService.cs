using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Vector2LevelEditor.Diagnostics;

namespace Vector2LevelEditor.Services;

public static class CustomContentService
{
    public static string DefaultStorageRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Nekki", "Vector 2");

    public static CustomContentFolders EnsureFolders(string? customRoomsDirectory = null)
    {
        var root = StorageRootFromRooms(customRoomsDirectory);
        var folders = new CustomContentFolders(
            root,
            Path.Combine(root, "custom_rooms"),
            Path.Combine(root, "custom_textures"),
            Path.Combine(root, "custom_backgrounds"));
        Directory.CreateDirectory(folders.Rooms);
        Directory.CreateDirectory(Path.Combine(folders.Rooms, "zone_2"));
        Directory.CreateDirectory(folders.Textures);
        Directory.CreateDirectory(folders.Backgrounds);
        return folders;
    }

    public static string ImportTexture(string sourcePath, string destinationFolder)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Texture file was not found.", sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
            throw new InvalidDataException("Vector 2 custom textures must be PNG, JPG, JPEG, or BMP files.");

        // Decode before copying so a renamed/corrupt file cannot poison the game texture folder.
        using (var stream = File.OpenRead(sourcePath))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth < 1 || decoder.Frames[0].PixelHeight < 1)
                throw new InvalidDataException("The selected file does not contain a readable image.");
        }

        Directory.CreateDirectory(destinationFolder);
        var stem = Regex.Replace(Path.GetFileNameWithoutExtension(sourcePath).Trim(), @"[^A-Za-z0-9_.-]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(stem)) stem = "custom_texture";
        var destination = Path.Combine(destinationFolder, stem + extension);
        File.Copy(sourcePath, destination, overwrite: true);
        DiagnosticsLog.Info($"Imported custom texture '{Path.GetFileName(destination)}'. ClassName='{stem}'.");
        return destination;
    }

    private static string StorageRootFromRooms(string? customRoomsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(customRoomsDirectory))
        {
            var full = Path.GetFullPath(customRoomsDirectory.Trim());
            if (Path.GetFileName(full).Equals("custom_rooms", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(full) ?? DefaultStorageRoot;
        }
        return DefaultStorageRoot;
    }
}

public sealed record CustomContentFolders(string Root, string Rooms, string Textures, string Backgrounds);
