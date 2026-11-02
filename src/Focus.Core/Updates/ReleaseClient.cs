using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Focus.Core.Updates;

public sealed record UpdateRelease(Version Version, string Name, Uri DownloadUrl, long Size, string? Sha256, Uri? ChecksumUrl);

/// <summary>Stable, x64 releases from the project's public GitHub repository.</summary>
public sealed class ReleaseClient(HttpClient http)
{
    public const string Repository = "https://github.com/AvroraCL/Focus";
    public const long MaxDownloadBytes = 512L * 1024 * 1024;

    public static Version? ParseVersion(string tag)
    {
        var match = Regex.Match(tag, @"^v?(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);
        return match.Success && Version.TryParse(tag.TrimStart('v'), out var version) ? version : null;
    }

    public static Uri TrustedAsset(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !uri.AbsolutePath.StartsWith("/AvroraCL/Focus/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Untrusted release download address.");
        return uri;
    }

    public static UpdateRelease? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var version = ParseVersion(root.GetProperty("tag_name").GetString() ?? "");
        if (version is null || version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        string name = $"Focus-v{version}-win-x64.zip";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var asset = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The release has no compatible win-x64 update package.");
        long size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxDownloadBytes) throw new InvalidDataException("Invalid update package size.");
        string? hash = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null;
        hash = hash?.StartsWith("sha256:", StringComparison.Ordinal) == true ? hash[7..] : null;
        if (hash is not null && !IsHash(hash)) throw new InvalidDataException("Invalid SHA-256 digest.");
        var checksum = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name + ".sha256");
        Uri? checksumUrl = checksum.ValueKind == JsonValueKind.Undefined ? null : TrustedAsset(checksum.GetProperty("browser_download_url").GetString()!);
        if (hash is null && checksumUrl is null) throw new InvalidDataException("The release is missing its SHA-256 checksum.");
        return new(version, name, TrustedAsset(asset.GetProperty("browser_download_url").GetString()!), size, hash, checksumUrl);
    }

    public async Task<UpdateRelease?> CheckAsync(Version current, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/AvroraCL/Focus/releases/latest");
        request.Headers.UserAgent.ParseAdd("Focus/" + current.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(token), current);
    }

    public async Task<string> DownloadAsync(UpdateRelease release, string destination, IProgress<double>? progress, CancellationToken token)
    {
        string hash = release.Sha256 ?? "";
        if (!IsHash(hash))
        {
            using var checksumResponse = await http.GetAsync(release.ChecksumUrl ?? throw new InvalidDataException("Missing checksum."), HttpCompletionOption.ResponseHeadersRead, token);
            checksumResponse.EnsureSuccessStatusCode();
            await using var checksumStream = await checksumResponse.Content.ReadAsStreamAsync(token);
            byte[] buffer = new byte[4097];
            int count = await checksumStream.ReadAtLeastAsync(buffer, buffer.Length, false, token);
            if (count > 4096) throw new InvalidDataException("Checksum document is too large.");
            hash = System.Text.Encoding.UTF8.GetString(buffer, 0, count).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!IsHash(hash)) throw new InvalidDataException("Invalid SHA-256 checksum.");
        }
        string partial = destination + ".partial";
        try
        {
            using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > release.Size || total > MaxDownloadBytes) throw new InvalidDataException("Download exceeded the advertised size.");
                    await file.WriteAsync(buffer.AsMemory(0, count), token);
                    progress?.Report(total * 100.0 / release.Size);
                }
                if (total != release.Size) throw new InvalidDataException("Incomplete update download.");
            }
            await using (var file = File.OpenRead(partial))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum verification failed.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination);
            return hash;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private static bool IsHash(string value) => Regex.IsMatch(value, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
}
