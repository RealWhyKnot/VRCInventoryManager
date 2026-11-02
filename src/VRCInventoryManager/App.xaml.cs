namespace VRCInventoryManager;

internal sealed partial class App : System.Windows.Application
{
    private Core.DebugLog _debugLog = Core.DebugLog.Disabled;

    internal static Core.DebugLog Log =>
        Current is App app ? app._debugLog : Core.DebugLog.Disabled;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _debugLog = Core.DebugLog.CreateNearExecutable(TimeProvider.System);
        Log.Info("Application starting.");

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception.", args.Exception);
            System.Windows.MessageBox.Show(
                args.Exception.Message,
                "VRCInventoryManager error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Error("Unhandled process exception.", exception);
            }
            else
            {
                Log.Error($"Unhandled process exception object: {args.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        Log.Info($"Application exiting with code {e.ApplicationExitCode}.");
        _debugLog.Dispose();
        base.OnExit(e);
        Updater.Relaunch();
    }
}
