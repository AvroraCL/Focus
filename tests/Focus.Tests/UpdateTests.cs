using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Focus.Core.Updates;
using Xunit;

namespace Focus.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Focus-update-tests-" + Guid.NewGuid().ToString("N"));
    public UpdateTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string FileAt(string name) => Path.Combine(_root, name);
    private static string Json(string tag = "v0.0.4", string? digest = null, bool prerelease = false, string? url = null) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft = false, prerelease,
        assets = new[] { new { name = $"Focus-{tag}-win-x64.zip", size = 3, digest = digest ?? "sha256:" + new string('A', 64), browser_download_url = url ?? $"https://github.com/AvroraCL/Focus/releases/download/{tag}/Focus-{tag}-win-x64.zip" } },
    });

    [Theory]
    [InlineData("v0.0.3")]
    [InlineData("v0.0.2")]
    [InlineData("v0.0.4-beta.1")]
    public void DoesNotDowngradeOrInstallPrerelease(string version) => Assert.Null(ReleaseClient.ParseRelease(Json(version), new Version(0, 0, 3, 0)));

    [Fact]
    public void NewStableReleaseMatchesExactAsset()
    {
        Assert.Equal(new Version(0, 0, 4), ReleaseClient.ParseRelease(Json(), new Version(0, 0, 3))!.Version);
        Assert.Null(ReleaseClient.ParseRelease(Json(prerelease: true), new Version(0, 0, 3)));
        Assert.Throws<InvalidDataException>(() => ReleaseClient.ParseRelease(Json(digest: "md5:123"), new Version(0, 0, 3)));
    }

    [Theory]
    [InlineData("http://github.com/AvroraCL/Focus/releases/download/v1/a.zip")]
    [InlineData("https://evil.test/AvroraCL/Focus/releases/download/v1/a.zip")]
    [InlineData("https://github.com/other/Focus/releases/download/v1/a.zip")]
    public void RejectsUntrustedRelease(string url) => Assert.Throws<InvalidDataException>(() => ReleaseClient.ParseRelease(Json(url: url), new Version(0, 0, 3)));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }

    [Fact]
    public async Task NoPublishedReleaseIsNotAnError()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        Assert.Null(await new ReleaseClient(http).CheckAsync(new Version(0, 0, 3), default));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task ServerErrorsAreNotReportedAsCurrent(int status)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)status)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseClient(http).CheckAsync(new Version(0, 0, 3), default));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DownloadVerifiesHashAndSize(bool badHash, bool truncated)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("test");
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var release = new UpdateRelease(new Version(0, 0, 4), "a.zip", new Uri("https://github.com/AvroraCL/Focus/releases/download/v0.0.4/a.zip"), truncated ? 8 : 4, badHash ? new string('0', 64) : hash, null);
        string dest = FileAt("package.zip");
        if (badHash || truncated)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(http).DownloadAsync(release, dest, null, default));
            Assert.False(File.Exists(dest));
        }
        else
        {
            await new ReleaseClient(http).DownloadAsync(release, dest, null, default);
            Assert.Equal(bytes, File.ReadAllBytes(dest));
        }
        Assert.False(File.Exists(dest + ".partial"));
    }

    [Fact]
    public async Task CancellationLeavesNoPackage()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var release = new UpdateRelease(new Version(0, 0, 4), "a.zip", new Uri("https://github.com"), 4, new string('0', 64), null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReleaseClient(http).DownloadAsync(release, FileAt("a.zip"), null, cancel.Token));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task FallsBackToPublishedChecksumFile()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("test");
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                ? new StringContent(hash + "  package.zip\n") : new ByteArrayContent(bytes),
        }));
        var release = new UpdateRelease(new Version(0, 0, 4), "package.zip", new Uri("https://github.com/AvroraCL/Focus/releases/download/v0.0.4/package.zip"), 4, null,
            new Uri("https://github.com/AvroraCL/Focus/releases/download/v0.0.4/package.zip.sha256"));
        Assert.Equal(hash, await new ReleaseClient(http).DownloadAsync(release, FileAt("package.zip"), null, default));
    }

    [Fact]
    public async Task DownloadCannotExceedAdvertisedSize()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("too large") }));
        var release = new UpdateRelease(new Version(0, 0, 4), "a.zip", new Uri("https://github.com"), 4, new string('0', 64), null);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ReleaseClient(http).DownloadAsync(release, FileAt("a.zip"), null, default));
        Assert.Empty(Directory.GetFiles(_root));
    }

    private (string Zip, string Hash) Package(params string[] names)
    {
        string path = FileAt(Guid.NewGuid() + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (string name in names) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("new"); }
        return (path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("/outside.exe")]
    [InlineData("C:/outside.exe")]
    [InlineData("overlay/../../outside.exe")]
    [InlineData("overlay\\outside.exe")]
    [InlineData("Focus.exe:payload")]
    [InlineData("Focus.exe.")]
    public void RejectsUnsafeArchivePaths(string name)
    {
        var package = Package(name);
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractVerified(package.Zip, package.Hash, FileAt("stage")));
        Assert.False(File.Exists(FileAt("outside.exe")));
    }

    [Fact]
    public void RejectsDuplicateAndIncompleteArchives()
    {
        var duplicate = Package("Focus.exe", "FOCUS.EXE");
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractVerified(duplicate.Zip, duplicate.Hash, FileAt("duplicate")));
        var incomplete = Package("Focus.exe");
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractVerified(incomplete.Zip, incomplete.Hash, FileAt("incomplete")));
        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractVerified(incomplete.Zip, new string('0', 64), FileAt("badHash")));
    }

    [Fact]
    public void InstallsCompletePackageAndPreservesUnrelatedFiles()
    {
        var package = Package("Focus.exe", "Focus.dll", "Focus.runtimeconfig.json", "focus_native.dll", "overlay/FocusOverlay.exe");
        UpdatePackage.ExtractVerified(package.Zip, package.Hash, FileAt("stage"));
        Directory.CreateDirectory(FileAt("app"));
        File.WriteAllText(FileAt("app/Focus.exe"), "old");
        File.WriteAllText(FileAt("app/user.txt"), "keep");
        UpdatePackage.Apply(FileAt("stage"), FileAt("app"), FileAt("backup"));
        Assert.Equal("new", File.ReadAllText(FileAt("app/Focus.exe")));
        Assert.Equal("old", File.ReadAllText(FileAt("backup/Focus.exe")));
        Assert.Equal("keep", File.ReadAllText(FileAt("app/user.txt")));
    }

    [Fact]
    public void FailedCopyRestoresPreviouslyOverwrittenFiles()
    {
        Directory.CreateDirectory(FileAt("stage")); Directory.CreateDirectory(FileAt("app"));
        File.WriteAllText(FileAt("stage/Focus.exe"), "new");
        File.WriteAllText(FileAt("stage/z-blocked.dll"), "new");
        File.WriteAllText(FileAt("app/Focus.exe"), "old");
        // A directory colliding with the second file forces failure after the first replacement.
        Directory.CreateDirectory(FileAt("app/z-blocked.dll"));
        Assert.Throws<AggregateException>(() => UpdatePackage.Apply(FileAt("stage"), FileAt("app"), FileAt("backup")));
        Assert.Equal("old", File.ReadAllText(FileAt("app/Focus.exe")));
    }
}
