using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace AulaKb.App;

public partial class App : Application
{
    static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AulaKb", "crash.log");

    Window? _window;

    public App()
    {
        InitializeComponent();
        // The UI language must be decided before the first resource is read: follows the
        // Windows preferred language unless the user picked one on the Settings page.
        if (Controls.AppSettings.Load().Language is { Length: > 0 } language)
            ApplicationLanguages.PrimaryLanguageOverride = language;
        UnhandledException += (_, e) => Log("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("Task", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>Applies next start-up; restart the app to be translated straightforwardly.</summary>
    public static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Current.Exit();
    }

    static void Log(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            File.AppendAllText(CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\n\n");
        }
        catch (Exception)
        {
        }
    }
}
