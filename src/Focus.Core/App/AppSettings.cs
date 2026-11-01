namespace Focus.Core.App;

public sealed class AppSettings
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Language { get; set; } = "system"; // "system", "zh-CN", "en-US"

    public string? ActiveProfileId { get; set; }
    public Dictionary<string, string> GameBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HotkeySettings Hotkeys { get; set; } = new();
    public bool ShowHudOnStartup { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool OnlyWhenFocused { get; set; } = true;

    public int HardwarePollMs { get; set; } = 500;
    public int UiRefreshHz { get; set; } = 12;

    public bool RecordAutomatically { get; set; }
    public string? LastTargetExecutable { get; set; }

    public bool OnboardingCompleted { get; set; }

    // Window placement, remembered between launches.
    public double WindowWidth { get; set; } = 1240;
    public double WindowHeight { get; set; } = 800;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }

    public AppSettings Normalize()
    {
        SchemaVersion = CurrentSchema;
        if (Language is not ("system" or "zh-CN" or "en-US")) Language = "system";
        HardwarePollMs = Math.Clamp(HardwarePollMs, 200, 5000);
        WindowWidth = double.IsNaN(WindowWidth) ? 1240 : Math.Clamp(WindowWidth, 1080, 8000);
        WindowHeight = double.IsNaN(WindowHeight) ? 800 : Math.Clamp(WindowHeight, 680, 8000);
        UiRefreshHz = Math.Clamp(UiRefreshHz, 1, 30);
        GameBindings ??= new(StringComparer.OrdinalIgnoreCase);
        Hotkeys ??= new();
        Hotkeys.ToggleHud.Normalize();
        Hotkeys.ToggleRecording.Normalize();
        Hotkeys.CycleProfile.Normalize();
        return this;
    }
}

public sealed class HotkeySettings
{
    public HotkeyBinding ToggleHud { get; set; } = new() { Control = true, Alt = true, VirtualKey = 0x48 }; // Ctrl+Alt+H
    public HotkeyBinding ToggleRecording { get; set; } = new() { Control = true, Alt = true, VirtualKey = 0x52 }; // Ctrl+Alt+R
    public HotkeyBinding CycleProfile { get; set; } = new() { Control = true, Alt = true, VirtualKey = 0x4C }; // Ctrl+Alt+L
}

public sealed class HotkeyBinding
{
    public bool Enabled { get; set; } = true;
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public int VirtualKey { get; set; }

    public void Normalize()
    {
        if (VirtualKey is < 1 or > 254) VirtualKey = 0;
        if (VirtualKey == 0) Enabled = false;
    }
}
