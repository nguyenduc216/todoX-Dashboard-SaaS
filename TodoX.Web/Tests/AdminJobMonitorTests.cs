using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AdminJobMonitorTests
{
    private static string Page() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
        "Components", "Pages", "AdminJobMonitor.razor"), Encoding.UTF8);

    [Theory]
    [InlineData(TodoXUserRole.Admin, true)]
    [InlineData(TodoXUserRole.SystemOperator, true)]
    [InlineData(TodoXUserRole.CustomerOwner, false)]
    [InlineData(TodoXUserRole.CustomerUser, false)]
    public void OnlyAdministratorsCanOpenTheMonitor(TodoXUserRole role, bool expected)
    {
        var user = new CurrentUserSession { IsAuthenticated = true, Role = role };
        Assert.Equal(expected, AdminEndpointAuthorization.IsAdmin(user));
        Assert.Equal(expected, NavigationAccessRules.CanAccessPath(user, "/admin/job-monitor"));
    }

    [Fact]
    public async Task ServiceRejectsNonAdministratorsBeforeTouchingTheDatabase()
    {
        var service = new AdminJobMonitorService(null!, null!);
        var customer = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetJobsAsync(customer));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetScenesAsync(customer, AdminJobMonitorKinds.RVideo, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("draft", AdminJobMonitorStatuses.Draft)]
    [InlineData("image_ready", AdminJobMonitorStatuses.Draft)]
    [InlineData("video_queued", AdminJobMonitorStatuses.Queued)]
    [InlineData("WAITING", AdminJobMonitorStatuses.Queued)]
    [InlineData("video_rendering", AdminJobMonitorStatuses.Processing)]
    [InlineData("RENDERING", AdminJobMonitorStatuses.Processing)]
    [InlineData("video_ready", AdminJobMonitorStatuses.Completed)]
    [InlineData("COMPLETED", AdminJobMonitorStatuses.Completed)]
    [InlineData("failed", AdminJobMonitorStatuses.Failed)]
    [InlineData("timeout", AdminJobMonitorStatuses.Failed)]
    [InlineData("INVALIDATED", AdminJobMonitorStatuses.Cancelled)]
    [InlineData("CANCELLED", AdminJobMonitorStatuses.Cancelled)]
    public void SceneStatusesOfEveryPipelineAreNormalized(string raw, string expected)
        => Assert.Equal(expected, AdminJobMonitorRules.NormalizeSceneStatus(raw));

    [Fact]
    public void QueryIsClampedAndBlankFiltersAreDropped()
    {
        var query = AdminJobMonitorRules.NormalizeQuery(new AdminJobMonitorQuery { Page = -3, PageSize = 5000, Search = "  ", Kind = "", Status = " " });
        Assert.Equal(1, query.Page);
        Assert.Equal(AdminJobMonitorRules.MaxPageSize, query.PageSize);
        Assert.Null(query.Search);
        Assert.Null(query.Kind);
        Assert.Null(query.Status);
    }

    [Theory]
    [InlineData(1, 1, "1")]
    [InlineData(1, 0, "1")]
    [InlineData(1, 5, "1,2,3,4,5")]
    [InlineData(1, 20, "1,2,3,0,20")]
    [InlineData(10, 20, "1,0,8,9,10,11,12,0,20")]
    [InlineData(20, 20, "1,0,18,19,20")]
    [InlineData(4, 20, "1,2,3,4,5,6,0,20")]
    [InlineData(5, 20, "1,2,3,4,5,6,7,0,20")]
    public void PageWindowShowsNumbersAroundCurrentAndBothEnds(int current, int total, string expected)
        => Assert.Equal(expected, string.Join(",", AdminJobMonitorRules.PageWindow(current, total)));

    [Fact]
    public void DateFiltersCoverTheWholeLocalDay()
    {
        var vietnam = TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
        var day = new DateOnly(2026, 10, 28);
        Assert.Equal(new DateTime(2026, 10, 27, 17, 0, 0, DateTimeKind.Utc), AdminJobMonitorRules.LocalDayStartUtc(day, vietnam));
        Assert.Equal(new DateTime(2026, 10, 28, 17, 0, 0, DateTimeKind.Utc), AdminJobMonitorRules.LocalDayEndUtc(day, vietnam));
    }

    // Regression: render.render_jobs is mostly worker rows (merge, scene image/video/audio, dance attempts)
    // and the old monitor listed them all as customer jobs.
    [Fact]
    public void ListSelectsOnlyCustomerJobsAndPutsUnfinishedNewestFirst()
    {
        var sql = AdminJobMonitorSql.JobsPage;
        Assert.Contains("j.job_type = 'core_service'", sql);
        Assert.Contains("j.job_type = 'timelapse'", sql);
        Assert.Contains("FROM dance_sell.dance_sell_jobs d", sql);
        Assert.DoesNotContain("merge_video_job", sql);
        Assert.DoesNotContain("j.job_type = 'dance_sell'", sql);
        Assert.Contains("ORDER BY unfinished DESC, created_at DESC, id DESC", sql);
    }

    [Fact]
    public void ListNeverLoadsScenesOrVideosBeforeAJobIsOpened()
    {
        var page = Page();
        Assert.Contains("if (firstRender) await LoadOnceAsync();", page);
        Assert.Equal(1, CountOf(page, "Monitor.GetScenesAsync("));
        var toggle = page[page.IndexOf("private async Task ToggleScenesAsync", StringComparison.Ordinal)..page.IndexOf("private Task RefreshAsync", StringComparison.Ordinal)];
        Assert.Contains("Monitor.GetScenesAsync(", toggle);
        Assert.Contains("if (_scenes.ContainsKey(job.Id))", toggle);
        // The only <video> element is the preview player, opened by an explicit click.
        Assert.Equal(1, CountOf(page, "<video "));
        Assert.Contains("@if (_previewUrl is not null)", page);
        Assert.Contains("loading=\"lazy\"", page);
    }

    [Fact]
    public void PageShowsEveryRequestedColumn()
    {
        var page = Page();
        foreach (var column in new[] { "Ảnh", "Tên job", "Loại job", "Dịch vụ", "Người thực hiện", "Ngày thực hiện", "Credit", "Trạng thái", "Số scene", "Video kết quả" })
            Assert.Contains($">{column}</th>", page);
        foreach (var column in new[] { "Hình ảnh", "Video", "Trạng thái", "Credit", "Ngày tạo", "Ngày hoàn thành" })
            Assert.Contains($">{column}</th>", page);
    }

    [Theory]
    [InlineData(AdminJobMonitorKinds.RVideo, "/jobs/rvideo/")]
    [InlineData(AdminJobMonitorKinds.RDance, "/jobs/rdance/")]
    [InlineData(AdminJobMonitorKinds.Timelapse, "/jobs/timelapse/")]
    public void OpenJobUsesTheCustomerRouteOfEachKind(string kind, string prefix)
    {
        var id = Guid.NewGuid();
        Assert.Equal(prefix + id, AdminJobMonitorRules.JobRoute(kind, id));
        var open = AdminJobMonitorRules.OpenJobPath(kind, id);
        Assert.True(NavigationAccessRules.IsAdminOnlyPath(open));
        Assert.False(NavigationAccessRules.CanAccessPath(new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner }, open));
    }

    [Fact]
    public void OpenJobLinkOpensANewTabThatKeepsTheAdminSession()
    {
        var page = Page();
        Assert.Contains("href=\"@AdminJobMonitorRules.OpenJobPath(job.Kind, job.Id)\" target=\"_blank\" rel=\"opener\"", page);
        var open = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Components", "Pages", "AdminJobMonitorOpen.razor"), Encoding.UTF8);
        Assert.Contains("\"vào từ giám sát video\"", open);
        Assert.Contains("Reason: {ImpersonationReason}", open);
        Assert.Contains("AdminEndpointAuthorization.IsAdmin(current)", open);
        Assert.True(open.IndexOf("Audit.LogAsync", StringComparison.Ordinal) < open.IndexOf("ImpersonateInThisTabAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("AuthState.ImpersonateAsync", open);
    }

    // Regression guard: tab-scoped impersonation must not remove the admin's remember-me marker.
    [Fact]
    public void TabImpersonationAndItsExitNeverTouchTheLocalMarker()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Services", "AuthStateService.cs"), Encoding.UTF8);
        var section = source[source.IndexOf("public async Task<bool> ImpersonateInThisTabAsync", StringComparison.Ordinal)..
                             source.IndexOf("/// Attempt to restore the session", StringComparison.Ordinal)];
        Assert.DoesNotContain("_local", section);
        Assert.DoesNotContain("SignInAsync", section);
    }

    private static int CountOf(string text, string value)
        => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    [AdminJobMonitorPostgresFact]
    public async Task RealQueriesReturnNormalizedJobsAndScenes()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(AdminJobMonitorPostgresFactAttribute.ConnectionVariable)!);
        Assert.Contains("test", settings.Database!, StringComparison.OrdinalIgnoreCase);

        var tenantId = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TodoXSaaS"] = settings.ConnectionString,
            ["TodoX:TenantId"] = tenantId.ToString()
        }).Build();
        var factory = new TodoXConnectionFactory(config);

        await using (var connection = new NpgsqlConnection(settings.ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(FixtureSchema);
            await connection.ExecuteAsync("INSERT INTO system.tenants(id, tenant_code, status) VALUES (@tenantId, 'default', 'active');", new { tenantId });
            await SeedAsync(connection, tenantId);
        }

        var service = new AdminJobMonitorService(factory, new TenantContext(factory, config));
        var admin = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.Admin };

        var page = await service.GetJobsAsync(admin);

        Assert.Equal(7, page.TotalCount);
        Assert.Equal(3, page.UnfinishedCount);
        Assert.Equal(
            ["rvideo-rendering", "dance-draft", "rvideo-draft", "rvideo-done", "timelapse-done", "dance-done", "rvideo-failed"],
            page.Items.Select(x => x.Title).ToArray());

        var running = page.Items.Single(x => x.Title == "rvideo-rendering");
        Assert.Equal(AdminJobMonitorStatuses.Processing, running.Status);
        Assert.Equal(2, running.SceneCount);
        Assert.Equal(4.5m, running.Credits);
        Assert.Equal("/media/scene-1.png", running.ThumbnailUrl);
        Assert.Equal("Dịch vụ RVideo", running.ServiceName);
        Assert.Equal("Người dùng", running.UserName);

        var rvideoDone = page.Items.Single(x => x.Title == "rvideo-done");
        Assert.Equal(AdminJobMonitorStatuses.Completed, rvideoDone.Status);
        Assert.Equal("/media/final.mp4", rvideoDone.ResultVideoUrl);
        Assert.Equal(AdminJobMonitorStatuses.Failed, page.Items.Single(x => x.Title == "rvideo-failed").Status);

        var timelapse = page.Items.Single(x => x.Title == "timelapse-done");
        Assert.Equal(AdminJobMonitorKinds.Timelapse, timelapse.Kind);
        Assert.Equal(AdminJobMonitorStatuses.Completed, timelapse.Status);
        Assert.Equal(5m, timelapse.Credits);
        Assert.Equal(1, timelapse.SceneCount);
        Assert.Equal("/media/tl-final-2.mp4", timelapse.ResultVideoUrl);
        Assert.Equal("/media/tl-original.png", timelapse.ThumbnailUrl);

        var dance = page.Items.Single(x => x.Title == "dance-done");
        Assert.Equal(AdminJobMonitorKinds.RDance, dance.Kind);
        Assert.Equal(12m, dance.Credits);
        Assert.Equal(1, dance.SceneCount);

        var rvideoScenes = await service.GetScenesAsync(admin, running.Kind, running.Id);
        Assert.Equal(2, rvideoScenes.Count);
        Assert.Equal(AdminJobMonitorStatuses.Completed, rvideoScenes[0].Status);
        Assert.Equal(4.5m, rvideoScenes[0].Credits);
        Assert.NotNull(rvideoScenes[0].CompletedAt);
        Assert.Equal(AdminJobMonitorStatuses.Processing, rvideoScenes[1].Status);
        Assert.Equal(0m, rvideoScenes[1].Credits);
        Assert.Null(rvideoScenes[1].CompletedAt);

        var timelapseScene = Assert.Single(await service.GetScenesAsync(admin, timelapse.Kind, timelapse.Id));
        Assert.Equal("/media/tl-stage-0.png", timelapseScene.ImageUrl);
        Assert.Equal("/media/tl-clip.mp4", timelapseScene.VideoUrl);
        Assert.Null(timelapseScene.Credits);

        var danceScene = Assert.Single(await service.GetScenesAsync(admin, dance.Kind, dance.Id));
        Assert.Equal(12m, danceScene.Credits);
        Assert.Equal("https://cdn.example/result.mp4", danceScene.VideoUrl);

        Assert.Equal(["rvideo-done", "rvideo-draft", "rvideo-failed", "rvideo-rendering"],
            (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Kind = AdminJobMonitorKinds.RVideo })).Items.Select(x => x.Title).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("timelapse-done",
            Assert.Single((await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Search = "timelapse-" })).Items).Title);
        Assert.Equal(2, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Status = AdminJobMonitorStatuses.Draft })).TotalCount);
        Assert.Equal(7, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Search = "nguoidung@example" })).TotalCount);
        var options = await service.GetFilterOptionsAsync(admin);
        Assert.Equal(["Dịch vụ RVideo", "Video nhảy quảng cáo thời trang", "Xây nhà"], options.Services.Order(StringComparer.Ordinal).ToArray());
        var optionUser = Assert.Single(options.Users);
        Assert.Equal("Người dùng (nguoidung@example.test)", optionUser.Label);
        Assert.Equal(4, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Service = "Dịch vụ RVideo" })).TotalCount);
        Assert.Equal(1, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Service = "Xây nhà" })).TotalCount);
        Assert.Equal(7, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { UserId = optionUser.Id })).TotalCount);
        Assert.Equal(0, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { UserId = Guid.NewGuid() })).TotalCount);
        var now = DateTime.UtcNow;
        // Jobs created 1h, 2h, 2h and 3h ago fall inside this window.
        Assert.Equal(4, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { FromUtc = now.AddHours(-3.5), ToUtc = now.AddMinutes(-30) })).TotalCount);
        Assert.Equal(0, (await service.GetJobsAsync(admin, new AdminJobMonitorQuery { ToUtc = now.AddHours(-30) })).TotalCount);

        Assert.Equal(optionUser.Id, await service.GetJobOwnerAsync(admin, running.Kind, running.Id));
        Assert.Equal(optionUser.Id, await service.GetJobOwnerAsync(admin, timelapse.Kind, timelapse.Id));
        Assert.Equal(optionUser.Id, await service.GetJobOwnerAsync(admin, dance.Kind, dance.Id));
        Assert.Null(await service.GetJobOwnerAsync(admin, AdminJobMonitorKinds.Timelapse, running.Id));
        Assert.Null(await service.GetJobOwnerAsync(admin, AdminJobMonitorKinds.RDance, Guid.NewGuid()));
        var customer = new CurrentUserSession { IsAuthenticated = true, Role = TodoXUserRole.CustomerOwner };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetJobOwnerAsync(customer, dance.Kind, dance.Id));

        // Regression: some jobs store a JSON settings blob as title and prompt; it must not be shown as the job name.
        var jsonJob = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(settings.ConnectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO render.render_jobs(id, tenant_id, service_id, job_type, operation_type, status, input_json)
                SELECT @jsonJob, @tenantId, id, 'core_service', 'rvideo', 'draft', jsonb_build_object('title', '{"aspect_ratio":"9:16"}')
                  FROM catalog.services LIMIT 1;
                INSERT INTO video_render.video_projects(tenant_id, core_job_id, title, original_prompt, status)
                VALUES (@tenantId, @jsonJob, '{"aspect_ratio":"9:16"}', '{"aspect_ratio":"9:16"}', 'draft');
                """,
                new { jsonJob, tenantId });
        }
        Assert.Equal("Dịch vụ RVideo", Assert.Single((await service.GetJobsAsync(admin, new AdminJobMonitorQuery { Search = jsonJob.ToString() })).Items).Title);
    }

    private static async Task SeedAsync(NpgsqlConnection connection, Guid tenant)
    {
        var user = Guid.NewGuid();
        var service = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO auth.app_users(id, tenant_id, full_name, username, email) VALUES (@user, @tenant, 'Người dùng', 'nguoidung', 'nguoidung@example.test');", new { user, tenant });
        await connection.ExecuteAsync("INSERT INTO catalog.services(id, service_code, service_name) VALUES (@service, 'RV', 'Dịch vụ RVideo');", new { service });

        var now = DateTime.UtcNow;
        async Task<Guid> RVideoAsync(string title, string coreStatus, string? projectStatus, int hoursAgo, string? finalUrl = null)
        {
            var job = Guid.NewGuid();
            await connection.ExecuteAsync(
                """
                INSERT INTO render.render_jobs(id, tenant_id, user_id, service_id, job_type, operation_type, status, input_json, created_at)
                VALUES (@job, @tenant, @user, @service, 'core_service', 'rvideo', @coreStatus, jsonb_build_object('title', @title::text), @created);
                """,
                new { job, tenant, user, service, coreStatus, title, created = now.AddHours(-hoursAgo) });
            if (projectStatus is not null)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO video_render.video_projects(tenant_id, user_id, core_job_id, title, original_prompt, status, final_video_url)
                    VALUES (@tenant, @user, @job, @title, 'prompt', @projectStatus, @finalUrl);
                    """,
                    new { tenant, user, job, title, projectStatus, finalUrl });
            }
            return job;
        }

        var rendering = await RVideoAsync("rvideo-rendering", "draft", "draft", 1);
        await RVideoAsync("rvideo-draft", "draft", "draft", 5);
        // The core row keeps a stale status; the project decides.
        await RVideoAsync("rvideo-done", "draft", "completed", 2, "/media/final.mp4");
        await RVideoAsync("rvideo-failed", "failed", "draft", 9);

        var project = await connection.QuerySingleAsync<long>("SELECT id FROM video_render.video_projects WHERE core_job_id=@rendering;", new { rendering });
        var sceneDone = await connection.QuerySingleAsync<long>(
            "INSERT INTO video_render.video_project_scenes(project_id, tenant_id, scene_index, static_image_url, scene_video_url, status) VALUES (@project, @tenant, 1, '/media/scene-1.png', '/media/scene-1.mp4', 'video_ready') RETURNING id;",
            new { project, tenant });
        await connection.ExecuteAsync(
            "INSERT INTO video_render.video_project_scenes(project_id, tenant_id, scene_index, status) VALUES (@project, @tenant, 2, 'video_rendering');",
            new { project, tenant });
        await connection.ExecuteAsync(
            "INSERT INTO video_render.scene_image_versions(project_id, scene_id, tenant_id, charged_points, status) VALUES (@project, @sceneDone, @tenant, 1.5, 'completed'), (@project, @sceneDone, @tenant, 100, 'failed');",
            new { project, sceneDone, tenant });
        var videoVersion = await connection.QuerySingleAsync<Guid>(
            "INSERT INTO video_render.scene_video_versions(project_id, scene_id, tenant_id, charged_points, status, completed_at) VALUES (@project, @sceneDone, @tenant, 2, 'completed', now()) RETURNING id;",
            new { project, sceneDone, tenant });
        await connection.ExecuteAsync(
            "INSERT INTO video_render.scene_audio_versions(project_id, scene_id, tenant_id, charged_points, status) VALUES (@project, @sceneDone, @tenant, 1, 'completed');",
            new { project, sceneDone, tenant });
        await connection.ExecuteAsync("UPDATE video_render.video_project_scenes SET selected_video_version_id=@videoVersion WHERE id=@sceneDone;", new { videoVersion, sceneDone });

        var timelapse = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO render.render_jobs(id, tenant_id, user_id, job_type, status, point_cost_charged, input_json, created_at)
            VALUES (@timelapse, @tenant, @user, 'timelapse', 'COMPLETED', 5,
                    '{"title":"timelapse-done","serviceName":"Xây nhà","originalImage":{"publicUrl":"/media/tl-original.png"}}'::jsonb, @created);
            """,
            new { timelapse, tenant, user, created = now.AddHours(-3) });
        await connection.ExecuteAsync("INSERT INTO timelapse.timelapse_image_stages(tenant_id, job_id, stage_index, progress_percent, is_original, status, public_url) VALUES (@tenant, @timelapse, 0, 0, false, 'COMPLETED', '/media/tl-stage-0.png');", new { tenant, timelapse });
        await connection.ExecuteAsync("INSERT INTO timelapse.timelapse_video_clips(tenant_id, job_id, clip_index, start_progress_percent, end_progress_percent, status, public_url) VALUES (@tenant, @timelapse, 0, 0, 100, 'COMPLETED', '/media/tl-clip.mp4');", new { tenant, timelapse });
        await connection.ExecuteAsync("INSERT INTO timelapse.timelapse_final_outputs(tenant_id, job_id, version, status, public_url) VALUES (@tenant, @timelapse, 1, 'COMPLETED', '/media/tl-final-1.mp4'), (@tenant, @timelapse, 2, 'COMPLETED', '/media/tl-final-2.mp4'), (@tenant, @timelapse, 3, 'FAILED', NULL);", new { tenant, timelapse });

        var danceDone = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO dance_sell.dance_sell_jobs(id, tenant_id, user_id, title, prompt, status, character_image_url, result_video_url, created_at, completed_at)
            VALUES (@danceDone, @tenant, @user, 'dance-done', 'p', 'completed', 'https://cdn.example/char.png', 'https://cdn.example/result.mp4', @created, now()),
                   (gen_random_uuid(), @tenant, @user, 'dance-draft', 'p', 'draft', 'https://cdn.example/char.png', NULL, @draftCreated, NULL);
            """,
            new { danceDone, tenant, user, created = now.AddHours(-4), draftCreated = now.AddHours(-2) });
        await connection.ExecuteAsync(
            """
            INSERT INTO dance_sell.dance_sell_provider_operations(dance_sell_job_id, operation_type, attempt_no, todox_points_charged, pricing_snapshot_json)
            VALUES (@danceDone, 'motion_video', 1, 99, '{}'::jsonb),
                   (@danceDone, 'motion_video', 2, 12, '{}'::jsonb),
                   (@danceDone, 'reference_image', 3, 77, '{}'::jsonb);
            """,
            new { danceDone });

        // Worker rows must never reach the list.
        foreach (var workerType in new[] { "merge_video_job", "render_scene_video", "render_scene_images", "render_scene_audio", "dance_sell", "render_video_job" })
        {
            await connection.ExecuteAsync(
                "INSERT INTO render.render_jobs(id, tenant_id, user_id, job_type, status, input_json) VALUES (gen_random_uuid(), @tenant, @user, @workerType, 'queued', '{}'::jsonb);",
                new { tenant, user, workerType });
        }

        // Another tenant's jobs must never reach the list.
        await connection.ExecuteAsync(
            "INSERT INTO dance_sell.dance_sell_jobs(id, tenant_id, title, prompt, status, character_image_url) VALUES (gen_random_uuid(), gen_random_uuid(), 'other-tenant', 'p', 'draft', 'x');");
    }

    private const string FixtureSchema = """
        DROP SCHEMA IF EXISTS system, auth, catalog, render, video_render, timelapse, dance_sell CASCADE;
        CREATE SCHEMA system; CREATE SCHEMA auth; CREATE SCHEMA catalog; CREATE SCHEMA render;
        CREATE SCHEMA video_render; CREATE SCHEMA timelapse; CREATE SCHEMA dance_sell;
        CREATE TABLE system.tenants (id uuid PRIMARY KEY, tenant_code text, status text);
        CREATE TABLE auth.app_users (id uuid PRIMARY KEY, tenant_id uuid, full_name text, display_name text, username text, email text);
        CREATE TABLE catalog.services (id uuid PRIMARY KEY, service_code text, service_name text);
        CREATE TABLE render.render_jobs (
            id uuid PRIMARY KEY, tenant_id uuid, user_id uuid, service_id uuid, job_type varchar(80) NOT NULL, operation_type text,
            status varchar(40) NOT NULL, input_json jsonb NOT NULL DEFAULT '{}', point_cost_charged numeric NOT NULL DEFAULT 0,
            created_at timestamptz NOT NULL DEFAULT now());
        CREATE TABLE video_render.video_projects (
            id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, tenant_id uuid NOT NULL, user_id uuid, core_job_id uuid, title text,
            original_prompt text NOT NULL, status text NOT NULL, final_video_url text, source_image_url text);
        CREATE TABLE video_render.video_project_scenes (
            id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, project_id bigint NOT NULL, tenant_id uuid NOT NULL, scene_index int NOT NULL,
            static_image_url text, scene_video_url text, status text NOT NULL, error_message text, selected_video_version_id uuid,
            created_at timestamptz NOT NULL DEFAULT now());
        CREATE TABLE video_render.scene_image_versions (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), project_id bigint NOT NULL, scene_id bigint NOT NULL, tenant_id uuid NOT NULL,
            charged_points numeric(18,6) NOT NULL DEFAULT 0, status text NOT NULL);
        CREATE TABLE video_render.scene_video_versions (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), project_id bigint NOT NULL, scene_id bigint NOT NULL, tenant_id uuid NOT NULL,
            charged_points numeric(18,6) NOT NULL DEFAULT 0, status text NOT NULL, completed_at timestamptz);
        CREATE TABLE video_render.scene_audio_versions (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), project_id bigint NOT NULL, scene_id bigint NOT NULL, tenant_id uuid NOT NULL,
            charged_points numeric(18,6) NOT NULL DEFAULT 0, status text NOT NULL);
        CREATE TABLE timelapse.timelapse_image_stages (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, job_id uuid NOT NULL, stage_index int NOT NULL,
            progress_percent int NOT NULL, is_original boolean NOT NULL DEFAULT false, status varchar(40) NOT NULL, public_url text);
        CREATE TABLE timelapse.timelapse_video_clips (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, job_id uuid NOT NULL, clip_index int NOT NULL,
            start_progress_percent int NOT NULL, end_progress_percent int NOT NULL, status varchar(40) NOT NULL, public_url text,
            error_message text, created_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz);
        CREATE TABLE timelapse.timelapse_final_outputs (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL, job_id uuid NOT NULL, version int NOT NULL,
            status varchar(40) NOT NULL, public_url text);
        CREATE TABLE dance_sell.dance_sell_jobs (
            id uuid PRIMARY KEY, tenant_id uuid, user_id uuid, title text, prompt text NOT NULL, status text NOT NULL,
            character_image_url text, prepared_reference_url text, result_video_url text, result_url text, error_message text,
            created_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz);
        CREATE TABLE dance_sell.dance_sell_provider_operations (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(), dance_sell_job_id uuid NOT NULL, operation_type text NOT NULL, attempt_no int NOT NULL,
            todox_points_charged numeric, pricing_snapshot_json jsonb, created_at timestamptz NOT NULL DEFAULT now());
        """;
}

public sealed class AdminJobMonitorPostgresFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "TODOX_JOBMONITOR_TEST_PG";

    public AdminJobMonitorPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            Skip = "Requires a disposable PostgreSQL database whose name contains 'test'; the test drops and recreates its schemas.";
    }
}
