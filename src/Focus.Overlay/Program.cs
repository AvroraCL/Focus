using System.Threading;
using System.Windows;

namespace Focus.Overlay;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        OverlayLog.Write("overlay start, instance=" + Focus.Core.FocusInstance.Suffix);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            OverlayLog.Write("fatal: " + (args.ExceptionObject as Exception));
        using var mutex = new Mutex(initiallyOwned: true, Focus.Core.FocusInstance.OverlayMutexName, out bool created);
        if (!created)
        {
            OverlayLog.Write("another overlay instance is running, exiting");
            return;
        }

        try
        {
            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose,
            };
            app.DispatcherUnhandledException += (_, args) =>
            {
                OverlayLog.Write("dispatcher exception: " + args.Exception);
                args.Handled = true;
            };

            var window = new OverlayWindow();
            app.MainWindow = window;
            window.Show();
            OverlayLog.Write("window shown");
            app.Run();
            OverlayLog.Write("overlay exit");
        }
        catch (Exception ex)
        {
            OverlayLog.Write("overlay failed: " + ex);
        }
    }
}
