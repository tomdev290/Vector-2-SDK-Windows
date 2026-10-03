using System.Collections.ObjectModel;
using System.Windows;

namespace Vector2LevelEditor.Diagnostics;

/// <summary>
/// Shared diagnostics pipe for the WPF port.
///
/// Why this exists:
/// The macOS editor was easy to debug because we could inspect Swift state while
/// building it. On Windows, missing textures, XML parser mismatches, and bad
/// export rules otherwise become silent black boxes. This log is the safety net:
/// importer, exporter, asset catalog, canvas rendering, and play bridge all write
/// human-readable messages here so port bugs are visible in-app.
/// </summary>
public static class DiagnosticsLog
{
    public static ObservableCollection<DiagnosticEntry> Entries { get; } = [];

    public static void Info(string message) => Add("INFO", message);
    public static void Warn(string message) => Add("WARN", message);
    public static void Error(string message) => Add("ERROR", message);

    public static void Clear() => RunOnUiThread(Entries.Clear);

    private static void Add(string level, string message)
    {
        RunOnUiThread(() =>
        {
            Entries.Add(new DiagnosticEntry(DateTime.Now, level, message));
            while (Entries.Count > 500)
            {
                Entries.RemoveAt(0);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
