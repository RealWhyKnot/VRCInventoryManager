namespace VRCInventoryManager;

public partial class App : System.Windows.Application
{
    private VRCInventoryManager.Core.DebugLog debugLog = VRCInventoryManager.Core.DebugLog.Disabled;

    internal static VRCInventoryManager.Core.DebugLog Log =>
        Current is App app ? app.debugLog : VRCInventoryManager.Core.DebugLog.Disabled;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        debugLog = VRCInventoryManager.Core.DebugLog.CreateNearExecutable();
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
        debugLog.Dispose();
        base.OnExit(e);
    }
}
