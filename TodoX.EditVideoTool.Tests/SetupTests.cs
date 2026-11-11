using System.Text.Json;
using TodoX.EditVideoTool.App;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Tests;

public sealed class SetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Installed_exe_finds_its_own_folder_only_when_that_folder_is_an_install()
    {
        var install = Path.Combine(_root, "TodoX EditVideo");
        var exe = Path.Combine(install, "app", AgentPaths.ExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);

        Assert.Null(AgentPaths.RootOfInstalledExe(exe));

        File.WriteAllText(Path.Combine(install, AgentPaths.MarkerFileName), "{}");
        Assert.Equal(install, AgentPaths.RootOfInstalledExe(exe));

        // A copy in Downloads or in bin\ is not an install, whatever its folder.
        Assert.Null(AgentPaths.RootOfInstalledExe(Path.Combine(install, AgentPaths.ExeName)));
        Assert.Null(AgentPaths.RootOfInstalledExe(Path.Combine(install, "app", "TodoX-EditVideo-Setup.exe")));
        Assert.Null(AgentPaths.RootOfInstalledExe(null));
    }

    [Fact]
    public void Pre_1_1_install_with_components_is_still_recognised()
    {
        var legacy = Path.Combine(_root, "EditVideoTool");
        Directory.CreateDirectory(Path.Combine(legacy, "components"));
        Directory.CreateDirectory(Path.Combine(legacy, "app"));

        Assert.Equal(legacy, AgentPaths.RootOfInstalledExe(Path.Combine(legacy, "app", AgentPaths.ExeName)));
    }

    [Theory]
    [InlineData(@"D:\Apps", @"D:\Apps\TodoX EditVideo")]
    [InlineData(@"D:\Apps\", @"D:\Apps\TodoX EditVideo")]
    [InlineData(@"D:\Apps\TodoX EditVideo", @"D:\Apps\TodoX EditVideo")]
    [InlineData(@"D:\Apps\todox editvideo", @"D:\Apps\todox editvideo")]
    public void Picked_folder_gets_the_product_folder_like_other_setups(string picked, string expected)
    {
        Assert.Equal(expected, SetupWizard.WithProductFolder(picked));
    }

    [Fact]
    public void Folder_check_rejects_relative_paths_and_drive_roots_and_accepts_a_writable_folder()
    {
        Assert.NotNull(Installer.CheckFolder("TodoX EditVideo"));
        Assert.NotNull(Installer.CheckFolder(""));
        Assert.NotNull(Installer.CheckFolder(@"C:\"));
        Assert.Null(Installer.CheckFolder(Path.Combine(_root, "Programs", "TodoX EditVideo")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Programs", "TodoX EditVideo")));
    }

    [Fact]
    public void Claude_choice_covers_desktop_and_code_but_not_codex()
    {
        Assert.True(ClientRegistrar.IsClaude("claude-desktop"));
        Assert.True(ClientRegistrar.IsClaude("claude-code"));
        Assert.False(ClientRegistrar.IsClaude("codex"));
    }

    [Fact]
    public void Install_log_records_each_step_and_is_saved_as_json_and_text()
    {
        var paths = new AgentPaths(_root);
        var log = new InstallLog("update", "1.0.0", _root, new InstallOptions(ConnectCodex: false));
        var changes = new List<InstallStep>();
        log.StepChanged += changes.Add;

        log.Start("copy", "Chép chương trình TodoX EditVideo");
        log.Done("copy", "xong");
        log.Start("component-ffmpeg", "Cài Bộ ghép & xử lý video");
        log.ProgressFor("component-ffmpeg").Report("Đang tải 50%");
        log.Fail("component-ffmpeg", "mất mạng");
        log.Skip("skills", "Tải bộ skill TodoX", "sẽ tải sau khi nhập license");
        log.Finish(ok: false);
        var text = log.Save(paths);

        Assert.Equal([StepState.Running, StepState.Done, StepState.Running, StepState.Failed, StepState.Skipped], changes.Select(c => c.State));
        Assert.Equal("Chép chương trình TodoX EditVideo", log.Steps.Single(s => s.Id == "copy").Title);
        Assert.Contains(log.Record.Entries, e => e.Step == "component-ffmpeg" && e.Message == "Đang tải 50%");
        Assert.Contains(log.Record.Entries, e => e.Level == "error" && e.Message.Contains("mất mạng"));

        var saved = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(Path.ChangeExtension(text, ".json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("update", saved.Mode);
        Assert.Equal("1.0.0", saved.FromVersion);
        Assert.False(saved.Ok);
        Assert.False(saved.Options!.ConnectCodex);
        Assert.False(saved.Uploaded);

        var readable = File.ReadAllText(text);
        Assert.Contains("cập nhật bản", readable);
        Assert.Contains("[component-ffmpeg] LỖI: mất mạng", readable);
        Assert.Contains("Kết quả: lỗi", readable);
    }

    [Fact]
    public async Task Install_logs_wait_on_the_machine_until_it_has_a_license()
    {
        var paths = new AgentPaths(_root);
        var log = new InstallLog("install", null, _root, new InstallOptions());
        log.Finish(ok: true);
        log.Save(paths);
        var server = new TodoXServerClient(new HttpClient(new FakeHandler()), new LicenseStore(paths), new FakeDevice());

        var sent = await new InstallLogUploader(server, paths).UploadPendingAsync(CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Single(Directory.GetFiles(paths.LogsDir, "install-*.json"));
    }
}
