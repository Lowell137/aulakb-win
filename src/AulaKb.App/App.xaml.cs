using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace AulaKb.App;

public partial class App : Application
{
    static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AulaKb", "crash.log");

    static Window? _window;

    /// <summary>Main window handle, for file pickers (unpackaged apps must pass it).</summary>
    public static IntPtr WindowHandle => _window is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(_window);

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

    // One instance: a second launch (Start menu while AulaKb waits in the tray) asks the
    // running one to show its window and quits.
    static Mutex? _single;
    const string ShowEventName = "AulaKb.ShowWindow";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool selftest = Environment.GetCommandLineArgs().Contains("--selftest");
        if (!selftest)
        {
            _single = new Mutex(true, "AulaKb.SingleInstance", out bool first);
            if (!first)
            {
                try
                {
                    using var show = EventWaitHandle.OpenExisting(ShowEventName);
                    show.Set();
                }
                catch (Exception)
                {
                }
                Exit();
                return;
            }
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            new Thread(() =>
            {
                while (showEvent.WaitOne())
                    queue.TryEnqueue(() => (_window as MainWindow)?.ShowFromTray());
            }) { IsBackground = true }.Start();
        }

        _window = new MainWindow();
        // Started with Windows (--tray): stay in the notification area until opened from there.
        if (Environment.GetCommandLineArgs().Contains("--tray"))
            _window.AppWindow.Hide();
        else
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
