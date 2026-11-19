using TodoX.Landing.Services;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>Landing "Tải app": only the build named by the app's manifest.json can be downloaded.</summary>
public sealed class LandingAppDownloadsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "landing-apps-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Release(string manifestJson, params string[] files)
    {
        var folder = Path.Combine(_root, "reupvideo");
        Directory.CreateDirectory(folder);
        foreach (var f in files)
        {
            File.WriteAllText(Path.Combine(folder, f), "exe");
        }

        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifestJson);
    }

    [Fact]
    public void LatestReadsAppFileFromManifest()
    {
        Release("""{"version":"1.0.0","publishedAt":"2026-11-19T09:00:00Z","files":[{"name":"todoX-ReupVideo-1.0.0.exe","kind":"app","sha256":"ABC123"}]}""", "todoX-ReupVideo-1.0.0.exe", "other.exe");

        var release = AppDownloads.Latest(_root, "reupvideo");

        Assert.NotNull(release);
        Assert.Equal("1.0.0", release!.Version);
        Assert.Equal("todoX-ReupVideo-1.0.0.exe", release.FileName);
        Assert.Equal(3, release.Size);
        Assert.Equal("ABC123", release.Sha256); // the desktop app checks it before updating itself
    }

    [Fact]
    public void NoFolderOrUnknownAppMeansNoDownload()
    {
        Assert.Null(AppDownloads.Latest(_root, "reupvideo"));
        Release("""{"version":"1.0.0","files":[{"name":"a.exe","kind":"app"}]}""", "a.exe");
        Assert.Null(AppDownloads.Latest(_root, "editvideo"));
        Assert.Null(AppDownloads.Latest(_root, "../reupvideo"));
    }

    [Fact]
    public void BrokenManifestOrMissingFileMeansNoDownload()
    {
        Release("{ not json", "a.exe");
        Assert.Null(AppDownloads.Latest(_root, "reupvideo"));

        Release("""{"version":"1.0.0","files":[{"name":"missing.exe","kind":"app"}]}""");
        Assert.Null(AppDownloads.Latest(_root, "reupvideo"));
    }

    [Theory]
    [InlineData("todoX-ReupVideo-1.0.0.exe", true)]
    [InlineData("../secret.exe", false)]
    [InlineData("..\\secret.exe", false)]
    [InlineData("C:secret.exe", false)]
    [InlineData("manifest.json", false)]
    [InlineData(".hidden.exe", false)]
    [InlineData("", false)]
    public void OnlyPlainExeNamesAreServed(string name, bool ok) => Assert.Equal(ok, AppDownloads.IsSafeFileName(name));

    [Fact]
    public void ManifestCannotPointOutsideTheAppFolder()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "secret.exe"), "x");
        Release("""{"version":"1.0.0","files":[{"name":"../secret.exe","kind":"app"}]}""");
        Assert.Null(AppDownloads.Latest(_root, "reupvideo"));
    }
}
