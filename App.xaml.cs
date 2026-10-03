using System.Windows;
using System.Windows.Threading;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Views;

namespace Vector2LevelEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Windows is way less forgiving than SwiftUI/AppKit when a binding,
        // image decode, or command throws. During the port we want those to hit
        // the in-app console instead of nuking the whole editor with no clue.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                DiagnosticsLog.Error($"Fatal app exception: {ex.Message}");
            }
        };

        base.OnStartup(e);

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Startup failed: {ex}");
            MessageBox.Show(
                ex.ToString(),
                "Vector 2 editor startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticsLog.Error($"UI exception: {e.Exception.Message}");
        DiagnosticsLog.Error(e.Exception.StackTrace ?? "No stack trace available.");
        e.Handled = true;
    }
}
