using System;
using System.Threading;
using System.Windows;
using Spotlight.App.Services;

namespace Spotlight.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Spotlight.SingleInstance";
    private const string ShowEventName = @"Local\Spotlight.ShowRequest";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second launch (Start Menu, installer, double click) just asks the running instance to open.
        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirstInstance)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            AppPaths.Log("Unhandled UI exception: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppPaths.Log("Unhandled exception: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppPaths.Log("Unobserved task exception: " + args.Exception);
            args.SetObserved();
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show(); // created off-screen and hidden again as soon as the UI has loaded

        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                window.Dispatcher.BeginInvoke(() => window.ShowWindow());
        })
        { IsBackground = true, Name = "Spotlight show-request listener" };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
