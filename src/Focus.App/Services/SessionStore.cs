using System.IO;
using Focus.Core.Sessions;

namespace Focus.App.Services;

public sealed class SessionStore
{
    private readonly List<PerformanceSession> _summaries = new();

    public event Action? Changed;

    public IReadOnlyList<PerformanceSession> Summaries => _summaries;

    public void Load()
    {
        _summaries.Clear();
        try
        {
            DataPaths.EnsureCreated();
            foreach (var file in Directory.EnumerateFiles(DataPaths.SessionsDir, "*.fss"))
            {
                var session = SessionFileCodec.LoadMetadataOnly(file);
                if (string.IsNullOrWhiteSpace(session.Id)) session.Id = Path.GetFileNameWithoutExtension(file);
                _summaries.Add(session);
            }
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("session scan failed: " + ex.Message);
        }

        _summaries.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
    }

    public void Save(PerformanceSession session)
    {
        try
        {
            DataPaths.EnsureCreated();
            SessionFileCodec.Save(session, DataPaths.SessionPath(session.Id));
            _summaries.RemoveAll(s => s.Id == session.Id);
            _summaries.Insert(0, session);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("session save failed: " + ex.Message);
            throw;
        }
    }

    public PerformanceSession? Load(string id)
    {
        try
        {
            var path = DataPaths.SessionPath(id);
            return File.Exists(path) ? SessionFileCodec.Load(path) : null;
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("session load failed: " + ex.Message);
            return null;
        }
    }

    public void Delete(string id)
    {
        _summaries.RemoveAll(s => s.Id == id);
        try
        {
            var path = DataPaths.SessionPath(id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("session delete failed: " + ex.Message);
        }
        Changed?.Invoke();
    }

    public void ExportCsv(PerformanceSession session, string path) => SessionFileCodec.ExportCsv(session, path);
}
