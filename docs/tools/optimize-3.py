import io

def patch(path, pairs, required=True):
    with io.open(path, encoding='utf-8') as f:
        s = f.read()
    for old, new in pairs:
        if old not in s:
            if required: print('MISS', path, '|', old[:60].replace('\n',' '))
            continue
        s = s.replace(old, new, 1)
    with io.open(path, 'w', encoding='utf-8') as f:
        f.write(s)

# 1) Dashboard honours the configured UI refresh rate and stops working while hidden
patch('src/Focus.App/Views/DashboardPage.xaml.cs', [
('''        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += (_, _) => Update();
        _timer.Start();''',
'''        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval() };
        _timer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            Update();
        };
        _timer.Start();'''),
('''        Loaded += (_, _) =>
        {
            Update();
            if (!_timer.IsEnabled) _timer.Start();
        };''',
'''        Loaded += (_, _) =>
        {
            _timer.Interval = RefreshInterval();
            Update();
            if (!_timer.IsEnabled) _timer.Start();
        };'''),
('''    private void Update()
    {''',
'''    private TimeSpan RefreshInterval()
    {
        int hz = Math.Clamp(_services.Settings.Current.UiRefreshHz, 5, 20);
        return TimeSpan.FromMilliseconds(1000.0 / hz);
    }

    private void Update()
    {'''),
])

# 2) Switching targets mid-recording would mix two processes into one session.
patch('src/Focus.App/Services/AppServices.cs', [
('''    private void OnGameChanged(ProcessInfo game)
    {
        var bound = Profiles.FindByGame(game.Name + ".exe");

        // Never steal a target the user picked by hand: auto-detection only fills an empty target
        // or applies an explicit game binding.
        if (Telemetry.TargetPid != game.Pid && (bound is not null || Telemetry.TargetPid == 0))
        {
            Telemetry.SetTarget(game.Pid, game.Name, game.ExecutablePath);
            TargetChanged?.Invoke();
        }''',
'''    private void OnGameChanged(ProcessInfo game)
    {
        var bound = Profiles.FindByGame(game.Name + ".exe");

        // Never steal a target the user picked by hand: auto-detection only fills an empty target
        // or applies an explicit game binding.
        if (Telemetry.TargetPid != game.Pid && (bound is not null || Telemetry.TargetPid == 0))
        {
            if (IsRecording) StopRecording();
            Telemetry.SetTarget(game.Pid, game.Name, game.ExecutablePath);
            TargetChanged?.Invoke();
        }'''),
('''    public void AttachTo(ProcessInfo process)
    {
        Telemetry.SetTarget(process.Pid, process.Name, process.ExecutablePath);''',
'''    public void AttachTo(ProcessInfo process)
    {
        if (IsRecording && Telemetry.TargetPid != process.Pid) StopRecording();
        Telemetry.SetTarget(process.Pid, process.Name, process.ExecutablePath);'''),
])

# 3) Normalise game bindings: accept paths / any case / missing extension
patch('src/Focus.App/Services/ProfileStore.cs', [
('''    public HudProfile? FindByGame(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        return _custom.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.GameExecutable) &&
            string.Equals(p.GameExecutable, executable, StringComparison.OrdinalIgnoreCase));
    }''',
'''    public HudProfile? FindByGame(string? executable)
    {
        string? name = NormalizeGameName(executable);
        if (name is null) return null;
        return _custom.FirstOrDefault(p =>
            string.Equals(NormalizeGameName(p.GameExecutable), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Accepts "game", "game.exe" or "C:\\Games\\game.exe" and returns "game.exe".</summary>
    public static string? NormalizeGameName(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        string name = executable.Trim().Trim('"');
        try
        {
            name = Path.GetFileName(name);
        }
        catch
        {
            // keep the raw value
        }
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        return name;
    }'''),
('''        Properties.AddText(Loc.Inst["profiles.bindGame"], _profile.GameExecutable ?? string.Empty,
            v => _profile.GameExecutable = string.IsNullOrWhiteSpace(v) ? null : v.Trim(), "game.exe");''',
'''        Properties.AddText(Loc.Inst["profiles.bindGame"], _profile.GameExecutable ?? string.Empty,
            v => _profile.GameExecutable = Focus.App.Services.ProfileStore.NormalizeGameName(v), "game.exe");'''),
])

# 4) Keep the overlay at least partially on screen when the HUD is larger than the display
patch('src/Focus.Overlay/OverlayWindow.cs', [
('''        _visible = visible;
        string key = $"{x:F0}:{y:F0}:{width:F0}:{height:F0}:{visible}";''',
'''        // Never let an oversized HUD drift completely off the display.
        x = Math.Clamp(x, bounds.Left - width + 48, bounds.Right - 48);
        y = Math.Clamp(y, bounds.Top - height + 48, bounds.Bottom - 48);

        _visible = visible;
        string key = $"{x:F0}:{y:F0}:{width:F0}:{height:F0}:{visible}";'''),
])

# 5) Settings page: short saved indicator
patch('src/Focus.App/Views/SettingsPage.xaml.cs', [
('''    private void SaveSettings()
    {
        if (_loading) return;
        _services.ApplySettings();
    }''',
'''    private void SaveSettings()
    {
        if (_loading) return;
        _services.ApplySettings();
        SavedIndicator.Text = Loc.Inst["settings.saved"];
        SavedIndicator.Opacity = 1;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(1400),
        };
        SavedIndicator.BeginAnimation(OpacityProperty, fade);
    }'''),
])
print('batch 3 done')
