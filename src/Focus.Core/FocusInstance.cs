namespace Focus.Core;

/// <summary>
/// Named objects (mutexes, shared memory) are suffixed so that several Focus instances can coexist
/// during development and testing. Normal runs use the "v1" suffix.
/// </summary>
public static class FocusInstance
{
    /// <summary>Set from the --instance command line switch before any other Focus type is used.</summary>
    public static string? Override { get; set; }

    public static string Suffix
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Override)) return Override.Trim();
            var value = Environment.GetEnvironmentVariable("FOCUS_INSTANCE");
            return string.IsNullOrWhiteSpace(value) ? "v1" : value.Trim();
        }
    }

    public static string AppMutexName => "Focus.App.SingleInstance." + Suffix;

    public static string OverlayMutexName => "Focus.Overlay.SingleInstance." + Suffix;

    public static string ActivateEventName => "Focus.App.Activate." + Suffix;
}
