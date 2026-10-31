using System.IO;

namespace Focus.Overlay;

internal static class OverlayLog
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(Path.GetTempPath(), "focus-overlay.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break the overlay.
        }
    }
}
