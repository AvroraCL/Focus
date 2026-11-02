using System.Diagnostics;
using System.Text.Json;
using Focus.Core.Updates;

namespace Focus.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? jobDirectory = null;
        string? target = null;
        bool applicationStopped = false;
        try
        {
            if (args.Length != 1) throw new ArgumentException("An update job is required.");
            string jobPath = Path.GetFullPath(args[0]);
            jobDirectory = Path.GetDirectoryName(jobPath)!;
            using var doc = JsonDocument.Parse(File.ReadAllText(jobPath));
            var job = doc.RootElement;
            target = job.GetProperty("Target").GetString()!;
            string archive = Path.Combine(jobDirectory, "package.zip");
            string stage = Path.Combine(jobDirectory, "extracted");
            string backup = Path.Combine(jobDirectory, "backup");
            // Verify and stage before waiting for the app. No installed file is changed yet.
            UpdatePackage.ExtractVerified(archive, job.GetProperty("Sha256").GetString()!, stage);
            File.WriteAllText(Path.Combine(jobDirectory, "ready"), "ready");
            try
            {
                using var parent = Process.GetProcessById(job.GetProperty("ParentId").GetInt32());
                if (parent.StartTime.ToUniversalTime().Ticks == job.GetProperty("ParentStarted").GetInt64() && !parent.WaitForExit(120000))
                    throw new TimeoutException("Focus did not exit. Close it and retry the update.");
            }
            catch (ArgumentException) { /* Parent already exited. */ }
            if (!File.Exists(Path.Combine(jobDirectory, "install"))) return 2;
            applicationStopped = true;
            // The overlay is shut down by AppServices; allow its final handle cleanup to finish.
            UpdatePackage.Apply(stage, target, backup);
            File.WriteAllText(Path.Combine(jobDirectory, "result.txt"), "Update completed.");
            Process.Start(new ProcessStartInfo(Path.Combine(target, "Focus.exe")) { UseShellExecute = true, WorkingDirectory = target });
            return 0;
        }
        catch (Exception ex)
        {
            if (jobDirectory is not null)
                try { File.WriteAllText(Path.Combine(jobDirectory, "error.txt"), ex.ToString()); } catch { }
            if (applicationStopped && target is not null && ex is AggregateException { InnerExceptions.Count: 1 })
                try { Process.Start(new ProcessStartInfo(Path.Combine(target, "Focus.exe")) { UseShellExecute = true, WorkingDirectory = target }); } catch { }
            MessageBox.Show("Focus 更新失败 / Update failed.\n备份与诊断文件 / Backup and diagnostics:\n" + jobDirectory + "\n\n" + ex.Message,
                "Focus update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
