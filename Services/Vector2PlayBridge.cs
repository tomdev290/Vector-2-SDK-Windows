using System.IO;
using System.Net.Http;
using System.Diagnostics;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Windows equivalent of the macOS Play button.
///
/// The Vector 2 mod understands a tiny editor handshake:
/// 1. Put the exported room XML in the game's custom_rooms folder.
/// 2. Write editor_launch_room.txt containing the room name.
/// 3. If the game is already open, send the localhost console command
///    "editorplay" so the game consumes that marker immediately.
///
/// This deliberately avoids hardcoded user paths. The env var is the clean
/// Windows-port escape hatch; the AppData fallbacks match the places Unity
/// commonly uses for persistent data.
/// </summary>
public sealed class Vector2PlayBridge
{
    private const string PreviewRoomName = "vector_editor_preview";
    private const string BundledGameFolder = "Vector 2 Game";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(1250) };
    private readonly Exporter _exporter;
    public string CustomRoomsOverride { get; set; } = "";
    public string GameExecutableOverride { get; set; } = "";

    public Vector2PlayBridge(Exporter exporter)
    {
        _exporter = exporter;
    }

    public async Task<string> PreviewAsync(LevelDocument document)
    {
        var roomName = PlayableRoomName(document);
        var xml = _exporter.Export(document);
        var executable = ResolveGameExecutable(GameExecutableOverride);
        if (executable.Length == 0) throw new IOException("Choose the working Vector 2 executable before pressing Play.");
        var dataFolder = Path.Combine(Path.GetDirectoryName(executable)!, Path.GetFileNameWithoutExtension(executable) + "_Data");
        var folder = Path.Combine(Vector2GameIntegrationLocator.ModDataRootFor(dataFolder), "custom_rooms");
        RunningGameExecutable(executable);

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, $"{roomName}.xml"), xml);
            await File.WriteAllTextAsync(Path.Combine(folder, "editor_launch_room.txt"), roomName);
            DiagnosticsLog.Info($"Wrote preview room '{roomName}' to {folder}.");
            LogDuplicateRoomNames(folder);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not write preview room to {folder}: {ex.Message}");
            throw new IOException($"Could not write Vector 2 preview room. {ex.Message}", ex);
        }

        if (executable.Length > 0 && IsGameAlreadyRunning(executable))
        {
            await Task.Delay(200);
            var answered = await NudgeConsoleAsync("editorplay");
            await Task.Delay(700);
            answered = await NudgeConsoleAsync("editorplay") || answered;
            if (!answered) throw new IOException("The room was exported, but the running game's editor console did not respond. Check that the selected game build is running and its local console is enabled.");
        }
        else
        {
            TryLaunchGame(executable);
        }
        return $"{roomName}.xml";
    }

    public static Task<bool> ReloadPlayerSaveAsync() => NudgeConsoleAsync("reload user");

    private static async Task<bool> NudgeConsoleAsync(string command)
    {
        var encoded = Uri.EscapeDataString(command);
        var answered = false;
        foreach (var port in new[] { 44444, 55055 })
        {
            var url = $"http://127.0.0.1:{port}/console/run?command={encoded}";
            try
            {
                using var response = await Http.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var rejected = body.Contains("unknown command", StringComparison.OrdinalIgnoreCase)
                      || body.Contains("command not found", StringComparison.OrdinalIgnoreCase)
                      || body.Contains("unknown arguments", StringComparison.OrdinalIgnoreCase)
                      || body.Contains("exception", StringComparison.OrdinalIgnoreCase);
                answered = answered || response.IsSuccessStatusCode && !rejected;
                DiagnosticsLog.Info($"Sent Vector 2 console command '{command}' to port {port}.");
                  if (answered) break;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Warn($"Vector 2 console port {port} did not answer: {ex.Message}");
            }
        }
        return answered;
    }

    private void TryLaunchGame(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            DiagnosticsLog.Warn("Vector 2 console did not answer and no game executable is configured.");
            return;
        }

        if (IsGameAlreadyRunning(executable))
        {
            DiagnosticsLog.Warn("Vector 2 appears to already be running, but the editor console did not answer. Not launching another game window.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
            DiagnosticsLog.Info($"Launched Vector 2 executable: {executable}");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not launch Vector 2 executable: {ex.Message}");
            throw new IOException("Could not launch selected game: " + executable, ex);
        }
    }

    public static string ResolveGameExecutable(string? configuredPath, string? applicationDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(applicationDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(applicationDirectory);
        var currentBundle = Path.Combine(root, BundledGameFolder, "Vector 2.exe");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            // Upgrade a previous editor bundle selection without overriding an external game choice.
            if (string.Equals(Path.GetFileName(Path.GetDirectoryName(configuredPath)), BundledGameFolder, StringComparison.OrdinalIgnoreCase)
                && File.Exists(currentBundle)) return currentBundle;
            return Path.GetFullPath(configuredPath);
        }
        var candidates = new[]
        {
            Path.Combine(root, BundledGameFolder, "Vector 2.exe"),
            Path.Combine(root, "Vector 2.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    private static bool IsGameAlreadyRunning(string executable)
    {
        try
        {
            var configuredName = Path.GetFileNameWithoutExtension(executable);
            if (string.IsNullOrWhiteSpace(configuredName)) return false;
            return RunningGameExecutable(executable) is not null;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not check running Vector 2 process: {ex.Message}");
            return false;
        }
    }

    private static string? RunningGameExecutable(string configured)
    {
        if (configured.Length == 0) return null;
        var paths = new List<string>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(configured)))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path && File.Exists(path)) paths.Add(path);
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        var unique = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return MatchRunningGame(configured, unique);
    }

    public static string? MatchRunningGame(string configured, IEnumerable<string> runningPaths)
    {
        var paths = runningPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Any(path => !Path.GetFullPath(path).Equals(Path.GetFullPath(configured), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A different Vector 2 copy is running. Close it before testing. Selected game: " + configured + "; running: " + string.Join(", ", paths));
        return paths.FirstOrDefault();
    }

    private static string PlayableRoomName(LevelDocument document)
    {
        // Keep editor Play isolated from real room names. The game resolves rooms
        // by name, so using the document filename can collide with nested
        // custom_rooms entries like testroom3.xml and zone_2/testroom3.xml.
        return PreviewRoomName;
    }

    private static string CandidateCustomRoomFolder(string overrideFolder)
    {
        if (!string.IsNullOrWhiteSpace(overrideFolder))
        {
            return Path.GetFullPath(overrideFolder);
        }

        var env = Environment.GetEnvironmentVariable("VECTOR2_CUSTOM_ROOMS_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow", "Nekki", "Vector 2", "custom_rooms"));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return Path.GetFullPath(Path.Combine(appData, "Nekki", "Vector 2", "custom_rooms"));
        }

        throw new DirectoryNotFoundException("Could not resolve a Vector 2 custom_rooms folder.");
    }

    private static void LogDuplicateRoomNames(string customRoomsFolder)
    {
        try
        {
            if (!Directory.Exists(customRoomsFolder)) return;

            var duplicates = Directory.EnumerateFiles(customRoomsFolder, "*.xml", SearchOption.AllDirectories)
                .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var duplicate in duplicates)
            {
                var paths = string.Join("; ", duplicate.Select(path => Path.GetRelativePath(customRoomsFolder, path)));
                DiagnosticsLog.Warn($"Duplicate custom room name '{duplicate.Key}' exists in custom_rooms: {paths}");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not scan custom_rooms for duplicate room names: {ex.Message}");
        }
    }
}
