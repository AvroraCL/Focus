using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

// No arguments: stand-in for the restarted Focus app in an isolated installation.
if (args.Length == 0)
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restarted.txt"), "restarted");
    return 0;
}
if (args[0] == "--parent")
{
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (!File.Exists(args[1]) && DateTime.UtcNow < deadline) await Task.Delay(100);
    return File.Exists(args[1]) ? 0 : 1;
}
if (args.Length != 2) throw new ArgumentException("Usage: UpdateCheck <helper directory> <test output directory>");
string helperSource = Path.GetFullPath(args[0]);
string root = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N"));
string target = Path.Combine(root, "app"), job = Path.Combine(root, "job"), helper = Path.Combine(job, "helper"), payload = Path.Combine(root, "payload");
Directory.CreateDirectory(target); Directory.CreateDirectory(job); Directory.CreateDirectory(helper); Directory.CreateDirectory(payload);
foreach (string file in Directory.GetFiles(helperSource)) File.Copy(file, Path.Combine(helper, Path.GetFileName(file)));
foreach (string file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(payload, Path.GetFileName(file)));
File.Copy(Path.Combine(payload, "UpdateCheck.exe"), Path.Combine(payload, "Focus.exe"));
File.WriteAllText(Path.Combine(payload, "Focus.dll"), "new");
File.WriteAllText(Path.Combine(payload, "Focus.runtimeconfig.json"), "{}");
File.WriteAllText(Path.Combine(payload, "focus_native.dll"), "fixture");
Directory.CreateDirectory(Path.Combine(payload, "overlay"));
File.WriteAllText(Path.Combine(payload, "overlay", "FocusOverlay.exe"), "fixture");
File.WriteAllText(Path.Combine(target, "Focus.exe"), "old-exe");
File.WriteAllText(Path.Combine(target, "Focus.dll"), "old");
File.WriteAllText(Path.Combine(target, "user-data.txt"), "preserve");
File.WriteAllText(Path.Combine(target, "retired.dll"), "old-component");
File.WriteAllLines(Path.Combine(target, "focus-installed-files.txt"), ["Focus.exe", "Focus.dll", "retired.dll"]);
string archive = Path.Combine(job, "package.zip");
ZipFile.CreateFromDirectory(payload, archive);
string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
string signal = Path.Combine(root, "exit-parent");
Process Start(string exe, params string[] arguments)
{
    var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
    foreach (string arg in arguments) start.ArgumentList.Add(arg);
    return Process.Start(start) ?? throw new Exception("Could not start " + exe);
}
using var parent = Start(Environment.ProcessPath!, "--parent", signal);
string jobPath = Path.Combine(job, "job.json");
File.WriteAllText(jobPath, JsonSerializer.Serialize(new { Target = target, Sha256 = hash, ParentId = parent.Id, ParentStarted = parent.StartTime.ToUniversalTime().Ticks }));
using var updater = Start(Path.Combine(helper, "FocusUpdater.exe"), jobPath);
try
{
    var deadline = DateTime.UtcNow.AddSeconds(45);
    while (!File.Exists(Path.Combine(job, "ready")))
    {
        if (DateTime.UtcNow > deadline || updater.HasExited) throw new Exception("Helper did not prepare: " + job);
        await Task.Delay(100);
    }
    if (File.ReadAllText(Path.Combine(target, "Focus.dll")) != "old") throw new Exception("Changed files before parent exited");
    File.WriteAllText(Path.Combine(job, "install"), "confirmed");
    File.WriteAllText(signal, "exit");
    if (!updater.WaitForExit(30000) || updater.ExitCode != 0) throw new Exception("Updater failed: " + job);
    while (!File.Exists(Path.Combine(target, "restarted.txt")) && DateTime.UtcNow < deadline) await Task.Delay(100);
    if (!File.Exists(Path.Combine(target, "restarted.txt"))) throw new Exception("Updated app did not restart");
    if (File.ReadAllText(Path.Combine(target, "Focus.dll")) != "new" || File.ReadAllText(Path.Combine(job, "backup", "Focus.dll")) != "old") throw new Exception("Replacement or backup failed");
    if (File.ReadAllText(Path.Combine(target, "user-data.txt")) != "preserve") throw new Exception("User data was changed");
    var owned = File.ReadAllLines(Path.Combine(target, "focus-installed-files.txt"));
    if (!owned.Contains("overlay/FocusOverlay.exe") || !owned.Contains("retired.dll") || owned.Contains("user-data.txt"))
        throw new Exception("Installed-file ownership was not maintained");
    Console.WriteLine("PASS: real helper staged, waited for parent exit, backed up, installed, preserved data and uninstall ownership, and restarted. " + root);
    return 0;
}
finally
{
    if (!parent.HasExited) parent.Kill();
    if (!updater.HasExited) updater.Kill();
}
