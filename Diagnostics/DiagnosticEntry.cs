namespace Vector2LevelEditor.Diagnostics;

/// <summary>
/// One line in the editor console. The Windows port intentionally keeps this
/// tiny because every subsystem can log here without taking a dependency on UI
/// controls. WPF binds to these entries in MainWindow.xaml; services just call
/// DiagnosticsLog.Info/Warn/Error when something important happens.
/// </summary>
public sealed record DiagnosticEntry(DateTime Time, string Level, string Message)
{
    public string Display => $"[{Time:HH:mm:ss}] {Level}: {Message}";
}
