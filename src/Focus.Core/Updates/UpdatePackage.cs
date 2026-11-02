using System.IO.Compression;
using System.Security.Cryptography;

namespace Focus.Core.Updates;

public static class UpdatePackage
{
    public static string ChildPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Unsafe update path: " + relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escapes its directory.");
        // Do not follow junctions / symlinks in either installed or staged trees.
        for (string? parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
            if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Update path contains a symbolic link or junction.");
        return path;
    }

    public static void ExtractVerified(string archive, string sha256, string stage)
    {
        using (var file = File.OpenRead(archive))
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum verification failed.");
        if (Directory.Exists(stage)) throw new IOException("Staging directory already exists.");
        Directory.CreateDirectory(stage);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 10000) throw new InvalidDataException("Too many update files.");
        long size = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            string name = entry.FullName.TrimEnd('/');
            string path = ChildPath(stage, name);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate update path.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Update contains a symbolic link.");
            size = checked(size + entry.Length);
            if (size > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Update expands beyond its size limit.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long written = 0;
            int count;
            while ((count = source.Read(buffer)) > 0)
            {
                written += count;
                if (written > entry.Length) throw new InvalidDataException("Archive entry exceeds its declared size.");
                destination.Write(buffer, 0, count);
            }
            if (written != entry.Length) throw new InvalidDataException("Truncated archive entry.");
        }
        foreach (string required in new[] { "Focus.exe", "Focus.dll", "Focus.runtimeconfig.json", "focus_native.dll", "overlay/FocusOverlay.exe" })
            if (!File.Exists(ChildPath(stage, required))) throw new InvalidDataException("Incomplete update package: " + required);
    }

    /// <summary>Back up each overwritten file, then roll back all changes if any replacement fails.</summary>
    public static void Apply(string stage, string target, string backup)
    {
        if (!File.Exists(Path.Combine(target, "Focus.exe"))) throw new IOException("The target is not a Focus installation.");
        if (Directory.Exists(backup)) throw new IOException("Backup directory already exists.");
        var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(stage, p).Replace('\\', '/')).ToArray();
        foreach (var relative in files) { ChildPath(stage, relative); ChildPath(target, relative); ChildPath(backup, relative); }
        Directory.CreateDirectory(backup);
        var applied = new List<(string Relative, bool Existed)>();
        try
        {
            foreach (var relative in files)
            {
                string dest = ChildPath(target, relative);
                bool existed = File.Exists(dest);
                if (existed)
                {
                    string saved = ChildPath(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(dest, saved);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                // Journal before writing: a failed copy can already have truncated its destination.
                applied.Add((relative, existed));
                File.Copy(ChildPath(stage, relative), dest, true);
            }
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                try
                {
                    string dest = ChildPath(target, item.Relative);
                    if (item.Existed) File.Copy(ChildPath(backup, item.Relative), dest, true);
                    else if (File.Exists(dest)) File.Delete(dest);
                }
                catch (Exception rollbackError) { errors.Add(rollbackError); }
            }
            throw new AggregateException("Update failed; backup retained at " + backup, errors);
        }
    }
}
