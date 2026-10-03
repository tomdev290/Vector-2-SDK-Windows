using System.Diagnostics;
using System.Windows;

namespace Vector2LevelEditor.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OpenYouTube_Click(object sender, RoutedEventArgs e) => Open("https://www.youtube.com/@Enderdude290");
    private void OpenVectorier_Click(object sender, RoutedEventArgs e) => Open("https://discord.com/invite/pVRuFBVwC2");
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
