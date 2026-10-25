using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed partial class SceneVideoOperationDraftPostgresTests
{
    private static async Task ExerciseKnownTaskHttpRecoveryAsync(NpgsqlConnection conn, TodoXConnectionFactory factory,
        IConfiguration config, RenderJobService jobs, VideoRenderRepository repo, IReadOnlyList<Guid> ids)
    {
        var baseUrl = Environment.GetEnvironmentVariable("TODOX_RECOVERY_SIM_URL")!;
        Assert.True(Uri.TryCreate(baseUrl, UriKind.Absolute, out var sim) && sim.IsLoopback);
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
        (await http.PostAsync("reset", null)).EnsureSuccessStatusCode();
        var tenant = new TenantContext(factory, config);
        var versions = new SceneMediaVersioningService(factory, tenant, config);
        var coordinator = new SceneVideoSubmitCoordinator(factory, tenant);
        var environment = new RecoveryEnvironment();
        var stored = new Dictionary<string, MediaFileDto>();
        var media = RecoveryProxy.Create<IMediaFileService>(asyncMedia);
        object? asyncMedia(MethodInfo method, object?[] args)
        {
            if (method.Name == "GetByObjectKeyAsync") return Task.FromResult(stored.GetValueOrDefault((string)args[1]!));
            if (method.Name == "DownloadAndSaveBinaryAtObjectKeyAsync") return DownloadAsync((string)args[0]!, (string)args[1]!);
            throw new InvalidOperationException("Unexpected storage operation " + method.Name);
        }
        async Task<MediaFileDto> DownloadAsync(string url, string key)
        {
            Assert.True(new Uri(url).IsLoopback);
            if (stored.TryGetValue(key, out var existing)) return existing;
            var bytes = await http.GetByteArrayAsync(url);
            Assert.True(bytes.Length > 20);
            var path = Path.Combine(environment.WebRootPath, "uploads", key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
            var saved = new MediaFileDto { Id = Guid.NewGuid(), ObjectKey = key, PublicUrl = "/uploads/" + key, FileSizeBytes = bytes.Length };
            stored[key] = saved; return saved;
        }
        var billingCompletions = 0;
        var billing = RecoveryProxy.Create<IAiImageBillingService>((method, _) =>
        {
            if (method.Name == "ReserveAsync") throw new InvalidOperationException("Recovery must not reserve a new submit");
            if (method.Name == "CompleteAsync") billingCompletions++;
            return RecoveryProxy.Default(method.ReturnType);
        });
        var lifecycle = RecoveryProxy.Create<IRVideoJobService>();
        var finalizer = RecoveryProxy.Create<IRVideoSceneMediaFinalizerService>();
        var completion = new RVideoSceneVideoCompletionService(media, versions,
            RecoveryProxy.Create<IRVideoSceneAudioAutoChainService>(), finalizer, lifecycle,
            RecoveryProxy.Create<IRVideoProjectFinalizationService>(), billing,
            new WalletService(factory, tenant, null!, NullLogger<WalletService>.Instance, null!), jobs, repo,
            tenant, config, NullLogger<RVideoSceneVideoCompletionService>.Instance);
        var video = RecoveryProxy.Create<IRVideo79AiVideoService>((method, args) =>
        {
            if (method.Name == "ResolveRuntimeAsync")
                return Task.FromResult(new RVideo79AiRuntime(1, 1, "79ai", baseUrl, "/forbidden-submit", "/video", "/forbidden-upload",
                    "mock", "196", new ResolvedProviderCredential { Secret = "fixture-only" }, null, null, 0));
            if (method.Name == "PollAsync")
                return new RVideo79AiVideoService(null!, null!, null!, new Ai79TaskClient(http), null!, config,
                    NullLogger<RVideo79AiVideoService>.Instance).PollAsync((RVideo79AiRuntime)args[0]!, (string)args[1]!, (CancellationToken)args[2]!);
            throw new InvalidOperationException("Recovery must never call " + method.Name);
        });
        var models = RecoveryProxy.Create<IAiProviderModelService>((method, _) => method.Name == "GetModelsAsync"
            ? Task.FromResult<IReadOnlyList<AiProviderModelListItemDto>>([]) : throw new InvalidOperationException(method.Name));
        var payer = RecoveryProxy.Create<IRVideoTrustedPayerContextService>((_, _) =>
            Task.FromResult(new AiBillingTrustedPayerContext("system", null, null, "fixture", "fixture")));
        using var logs = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var handler = new SceneVideoWorkerHandler(repo, coordinator, versions, billing, RecoveryProxy.Create<IAiProviderService>(), models,
            new VideoGenerationProviderAdapterResolver([new Ai79VideoGenerationProviderAdapter(video)]), media, new VideoPromptValidator(),
            jobs, payer, lifecycle, tenant, config, finalizer, completion, new RecoveryOptions(), logs.CreateLogger<SceneVideoWorkerHandler>());

        // Hold a poll lease while another handler tries the same task: no HTTP is issued by the loser.
        var leaseJob = await jobs.GetAsync(ids[0]);
        using (await coordinator.TryAcquireKnownTaskPollFenceAsync(ids[0], CancellationToken.None))
            await Assert.ThrowsAsync<RenderJobDeferredException>(() => handler.HandleAsync(leaseJob!, CancellationToken.None));
        for (var round = 0; round < 2; round++)
        {
            if (round > 0)
                foreach (var id in ids)
                    Assert.True(await jobs.SchedulePersistentProviderPollAsync(id, TimeSpan.FromSeconds(1), "RECOVERY", "Poll same task"));
            await conn.ExecuteAsync("UPDATE render.render_jobs SET retry_after=now()-interval '1 second' WHERE status='queued';");
            for (var index = 0; index < 8; index++)
            {
                var job = await jobs.ClaimNextByJobTypeAsync("recovery-fixture", TimeSpan.FromMinutes(1), [RenderJobTypes.RenderSceneVideo]);
                Assert.NotNull(job);
                await jobs.MarkSceneVideoClaimRenderingAsync(job, "recovery-fixture");
                if (round == 0)
                {
                    var pending = await Assert.ThrowsAsync<RenderJobPendingReconciliationException>(() => handler.HandleAsync(job, CancellationToken.None));
                    Assert.Contains("local polling timeout", pending.Message);
                    await jobs.MarkStatusAsync(job.Id, "pending_reconciliation", errorMessage: pending.Message);
                }
                else
                {
                    await handler.HandleAsync(job, CancellationToken.None);
                    await jobs.MarkStatusAsync(job.Id, "completed");
                }
                Assert.Equal(1, (await jobs.GetAsync(job.Id))!.AttemptCount);
            }
        }
        Assert.Equal(8, stored.Count);
        Assert.Equal(8, billingCompletions);
        Assert.Equal(8, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_jobs WHERE status='completed';"));
        Assert.Equal(8, await conn.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM render.render_jobs j JOIN video_render.scene_video_versions v ON v.render_job_id=j.id
            JOIN video_render.scene_video_operation_heads h ON h.active_render_job_id=j.id AND h.active_video_version_id=v.id
            JOIN render.scene_video_submit_intents i ON i.render_job_id=j.id AND i.logical_request_id=v.logical_request_id
            WHERE h.generation=1 AND j.provider_task_id='existing-task-'||v.scene_id::text
              AND j.provider_video_id_base='existing-base-'||v.scene_id::text
              AND v.provider_task_id=j.provider_task_id AND v.provider_video_id_base=j.provider_video_id_base
              AND i.provider_task_id=j.provider_task_id AND i.provider_video_id_base=j.provider_video_id_base
              AND i.submit_state='CONFIRMED_PROVIDER_TASK';
            """));
        Assert.Equal(8, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.video_project_scenes s JOIN video_render.scene_video_versions v ON v.id=s.selected_video_version_id WHERE s.status='video_ready' AND v.status='completed' AND v.is_selected AND v.result_media_id IS NOT NULL;"));
        using var metrics = JsonDocument.Parse(await http.GetStringAsync("metrics"));
        Assert.Equal(0, metrics.RootElement.GetProperty("submits").GetInt32());
        Assert.All(metrics.RootElement.GetProperty("polls").EnumerateObject(), poll => Assert.Equal(2, poll.Value.GetInt32()));
    }

    public class RecoveryProxy : DispatchProxy
    {
        private Func<MethodInfo, object?[], object?>? _callback;
        public static T Create<T>(Func<MethodInfo, object?[], object?>? callback = null) where T : class
        {
            var proxy = DispatchProxy.Create<T, RecoveryProxy>();
            ((RecoveryProxy)(object)proxy)._callback = callback;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => _callback is null ? Default(method!.ReturnType) : _callback(method!, args!);
        public static object? Default(Type type)
        {
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var result = type.GenericTypeArguments[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                    .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null]);
            }
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
    private sealed class RecoveryOptions : IOptionsMonitor<VideoRenderOptions>
    {
        public VideoRenderOptions CurrentValue { get; } = new();
        public VideoRenderOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<VideoRenderOptions, string?> listener) => null;
    }
    private sealed class RecoveryEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "RecoveryFixture";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "todox-recovery-" + Guid.NewGuid().ToString("N"));
        public string WebRootPath { get => Path.Combine(ContentRootPath, "wwwroot"); set { } }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
