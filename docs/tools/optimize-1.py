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

# 1) ETW: drop the auto-fallback (it captured every DxgKrnl event while idle)
patch('src/Focus.Telemetry/Frames/FrameCaptureService.cs', [
('''                _session.Source.AllEvents += OnEvent;
                _running = true;
                _processing = Task.Factory.StartNew(
                    () => _session?.Source.Process(),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                _watchdog = new Thread(Watchdog) { IsBackground = true, Name = "Focus-ETW-Watchdog" };
                _watchdog.Start();
                return true;''',
'''                _session.Source.AllEvents += OnEvent;
                _running = true;
                _processing = Task.Factory.StartNew(
                    () => _session?.Source.Process(),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                global::System.IO.File.AppendAllText(
                    global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "focus-etw.log"),
                    $"{DateTime.Now:HH:mm:ss.fff}  session started, event-id filter for Present_Info{Environment.NewLine}");
                return true;'''),
('''    private void EnableProvider(bool keywordFilter)
    {
        if (_session is null) return;
        // The Present keyword keeps the session cheap; if the platform ignores it we fall back to
        // capturing every DxgKrnl event and filter by event id in the callback.
        if (!keywordFilter)
        {
            _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Verbose, 0UL);
            return;
        }

        ulong keywords = KeywordOverride != 0 ? KeywordOverride : 0UL;''',
'''    private void EnableProvider()
    {
        if (_session is null) return;

        // Keywords do not match Present_Info on Windows 10; the kernel-level event id filter is what
        // keeps the session quiet, so every other DxgKrnl event is dropped before it reaches us.
        ulong keywords = KeywordOverride;'''),
('''                EnableProvider(_usingKeywordFilter);''', '''                EnableProvider();'''),
('''    private void Watchdog()
    {
        // If nothing at all arrives we are most likely filtering out the Present events.
        DateTime start = DateTime.UtcNow;
        while (_running)
        {
            Thread.Sleep(2000);
            if (!_running) return;
            if (_presentsSeen == 0 && _eventsSeen == 0 && DateTime.UtcNow - start > TimeSpan.FromSeconds(6) && _usingKeywordFilter)
            {
                try
                {
                    global::System.IO.File.AppendAllText(
                        global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "focus-etw.log"),
                        $"{DateTime.Now:HH:mm:ss.fff}  keyword filter produced no events, falling back{Environment.NewLine}");
                    _usingKeywordFilter = false;
                    EnableProvider(false);
                }
                catch
                {
                    // Keep running with whatever we have.
                }
                start = DateTime.UtcNow;
            }
        }
    }

''', ''),
('''    private bool _usingKeywordFilter = true;
''', ''),
('''    private Thread? _watchdog;
''', ''),
('''    private volatile int _eventsSeen;''', '''    private volatile int _eventsSeen;'''),
])

# 2) Settings: robust atomic save with retries and a final direct-write fallback
patch('src/Focus.App/Services/SettingsService.cs', [
('''    public void Save()
    {
        try
        {
            DataPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(Current, Options);
            if (File.Exists(DataPaths.SettingsFile))
            {
                File.Copy(DataPaths.SettingsFile, DataPaths.SettingsBackup, overwrite: true);
            }
            var temp = DataPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, DataPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings save failed: " + ex.Message);
        }
    }''',
'''    public void Save()
    {
        try
        {
            DataPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(Current, Options);
            var path = DataPaths.SettingsFile;

            if (File.Exists(path))
            {
                try
                {
                    File.Copy(path, DataPaths.SettingsBackup, overwrite: true);
                }
                catch (Exception ex)
                {
                    DataPaths.AppendLog("settings backup failed: " + ex.Message);
                }
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, json);

            // Antivirus / indexing services sometimes hold the file for a few milliseconds.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(40);
                }
                catch (UnauthorizedAccessException) when (attempt < 3)
                {
                    Thread.Sleep(40);
                }
            }

            // Last resort: write in place so the user's settings are never silently lost.
            File.WriteAllText(path, json);
            try { File.Delete(temp); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings save failed: " + ex.Message);
        }
    }'''),
])
print('batch 1 done')
