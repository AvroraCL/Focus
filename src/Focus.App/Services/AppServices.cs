using Focus.Core.Profiles;
using Focus.App.Localization;
using Focus.Telemetry;
using Focus.Telemetry.System;

namespace Focus.App.Services;

/// <summary>Composition root for the desktop app: settings, stores, telemetry and the overlay.</summary>
public sealed class AppServices : IDisposable
{
    public SettingsService Settings { get; } = new();
    public ProfileStore Profiles { get; } = new();
    public SessionStore Sessions { get; } = new();
    public TelemetryHub Telemetry { get; } = new();
    public OverlayController Overlay { get; private set; } = null!;
    public GameWatcher Games { get; private set; } = null!;
    public TelemetryDataSource HudData { get; private set; } = null!;

    public HudProfile ActiveProfile { get; private set; } = new();
    public bool IsElevated => ElevationHelper.IsElevated();
    public bool IsRecording => Telemetry.Recorder.IsRecording;
    public bool CaptureRunning => Telemetry.Capture.IsRunning;

    public event Action? ActiveProfileChanged;
    public event Action? RecordingChanged;
    public event Action? TargetChanged;
    public event Action? OverlayVisibilityChanged;

    public bool OverlayVisible
    {
        get => Overlay.Visible;
        set
        {
            if (Overlay.Visible == value) return;
            Overlay.Visible = value;
            Settings.Current.ShowHudOnStartup = value;
            Settings.Save();
            OverlayVisibilityChanged?.Invoke();
        }
    }

    public void Initialize()
    {
        DataPaths.EnsureCreated();
        Settings.Load();
        SettingsAccessor.Current = Settings;
        Loc.Inst.SetLanguage(Settings.Current.Language);

        Profiles.Load();
        Sessions.Load();

        Telemetry.HardwareIntervalMs = Settings.Current.HardwarePollMs;
        Telemetry.Start();
        DataPaths.AppendLog($"telemetry started: etw={Telemetry.Capture.IsRunning}, hardware={Telemetry.Hardware.IsRunning}, elevated={ElevationHelper.IsElevated()}");
        Telemetry.TargetLost += OnTargetLost;

        var profile = Profiles.Get(Settings.Current.ActiveProfileId)
                      ?? Profiles.Custom.FirstOrDefault()
                      ?? Profiles.Presets.First(p => p.Id == Focus.Hud.Presets.PresetLibrary.ClassicId);
        ActiveProfile = profile;
        Settings.Current.ActiveProfileId = profile.Id;
        Settings.Save();

        Overlay = new OverlayController(Telemetry, Settings);
        Overlay.SetProfile(ActiveProfile);
        Overlay.Visible = Settings.Current.ShowHudOnStartup;
        Overlay.Start();

        Games = new GameWatcher(Telemetry, Profiles);
        Games.GameChanged += OnGameChanged;
        Games.Start();

        HudData = new TelemetryDataSource(Telemetry);
    }

    private void OnTargetLost(int pid, string name)
    {
        if (IsRecording) StopRecording();
        TargetChanged?.Invoke();
    }

    private void OnGameChanged(ProcessInfo game)
    {
        var bound = Profiles.FindByGame(game.Name + ".exe");

        // Never steal a target the user picked by hand: auto-detection only fills an empty target
        // or applies an explicit game binding.
        if (Telemetry.TargetPid != game.Pid && (bound is not null || Telemetry.TargetPid == 0))
        {
            Telemetry.SetTarget(game.Pid, game.Name, game.ExecutablePath);
            TargetChanged?.Invoke();
        }

        if (bound is not null && bound.Id != ActiveProfile.Id) SetActiveProfile(bound);

        if (Settings.Current.RecordAutomatically && !IsRecording) StartRecording();
    }

    public void AttachTo(ProcessInfo process)
    {
        Telemetry.SetTarget(process.Pid, process.Name, process.ExecutablePath);
        Settings.Current.LastTargetExecutable = process.Name + ".exe";
        Settings.Save();
        var bound = Profiles.FindByGame(process.Name + ".exe");
        if (bound is not null) SetActiveProfile(bound);
        TargetChanged?.Invoke();
    }

    public void Detach()
    {
        if (IsRecording) StopRecording();
        Telemetry.ClearTarget();
        TargetChanged?.Invoke();
    }

    public void SetActiveProfile(HudProfile profile)
    {
        ActiveProfile = profile;
        Settings.Current.ActiveProfileId = profile.Id;
        Settings.Save();
        Overlay.SetProfile(profile);
        ActiveProfileChanged?.Invoke();
    }

    public void NotifyProfileSaved(HudProfile profile)
    {
        if (profile.Id == ActiveProfile.Id) ActiveProfile = profile;
        Overlay.SetProfile(profile);
        ActiveProfileChanged?.Invoke();
    }

    public void StartRecording()
    {
        if (IsRecording) return;
        Telemetry.Recorder.Start();
        RecordingChanged?.Invoke();
    }

    public Core.Sessions.PerformanceSession? StopRecording()
    {
        if (!IsRecording) return null;
        string name = $"{Telemetry.TargetName} {DateTime.Now:yyyy-MM-dd HH:mm}";
        var session = Telemetry.Recorder.Stop(name, Telemetry.TargetName, Telemetry.TargetPid, ActiveProfile.Id);
        try
        {
            Sessions.Save(session);
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("session save failed: " + ex.Message);
        }
        RecordingChanged?.Invoke();
        return session;
    }

    public void ToggleRecording()
    {
        if (IsRecording) StopRecording();
        else StartRecording();
    }

    public void CycleProfile()
    {
        var all = Profiles.Custom.Count > 0 ? Profiles.Custom.Concat(Profiles.Presets).ToList() : Profiles.Presets.ToList();
        if (all.Count == 0) return;
        int index = all.FindIndex(p => p.Id == ActiveProfile.Id);
        var next = all[(index + 1) % all.Count];
        SetActiveProfile(next);
    }

    public void ApplySettings()
    {
        Telemetry.HardwareIntervalMs = Settings.Current.HardwarePollMs;
        Loc.Inst.SetLanguage(Settings.Current.Language);
        Settings.Save();
    }

    public void Shutdown()
    {
        try
        {
            if (IsRecording) StopRecording();
        }
        catch
        {
            // ignore
        }
        Overlay?.Dispose();
        Games?.Dispose();
        Telemetry.Dispose();
        Settings.Save();
    }

    public void Dispose() => Shutdown();
}
