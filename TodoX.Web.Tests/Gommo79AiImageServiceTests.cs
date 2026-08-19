using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class Gommo79AiImageServiceTests
{
    [Fact]
    public async Task GeneratesWithNormalizedRatioAndPollsTheSameTaskIdOnce()
    {
        var client = new FakeClient(
            submitTaskIds: ["task-1"],
            statuses: [new Ai79TaskStatusResult("SUCCESS", """{"status":"SUCCESS","url":"https://cdn.example/one.jpg"}""", "https://cdn.example/one.jpg", null, null)]);
        var events = new List<string>();
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "construction scene",
            AspectRatio = "16:9",
            CapabilityConfigJson = """{"poll_max_attempts":1}""",
            ProgressCallback = (eventType, _) =>
            {
                events.Add(eventType);
                return Task.CompletedTask;
            }
        });

        Assert.True(result.Success);
        Assert.Equal("16_9", client.Submits.Single().Options["ratio"]);
        Assert.Equal(1, client.Submits.Count);
        Assert.Equal(["task-1"], client.PolledTaskIds);
        Assert.Contains("SCENE_IMAGE_PROVIDER_SUBMITTED", events);
        Assert.Contains("SCENE_IMAGE_READY", events);
        Assert.DoesNotContain("access_token", result.RawRequestJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsesConfiguredModelChainOnlyAfterTerminalFailure()
    {
        var client = new FakeClient(
            submitTaskIds: ["task-1", "task-2"],
            statuses:
            [
                new Ai79TaskStatusResult("FAILED", """{"status":"ERROR","error":"unavailable"}""", null, "provider_error", "unavailable"),
                new Ai79TaskStatusResult("SUCCESS", """{"status":"SUCCESS","url":"https://cdn.example/two.jpg"}""", "https://cdn.example/two.jpg", null, null)
            ]);
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "fallback scene",
            AspectRatio = "9:16",
            CapabilityConfigJson = """{"poll_max_attempts":1}"""
        });

        Assert.True(result.Success);
        Assert.Equal(["google_image_gen_banana_2", "imagegen_2_0"], client.Submits.Select(x => x.Model).ToArray());
        Assert.Equal(["task-1", "task-2"], client.PolledTaskIds);
        Assert.DoesNotContain("seedream_4_5", client.Submits.Select(x => x.Model));
    }

    [Fact]
    public async Task RecoversSuccessWithoutUrlThroughImageList()
    {
        var client = new FakeClient(
            submitTaskIds: ["task-1"],
            statuses: [new Ai79TaskStatusResult("SUCCESS", """{"status":"SUCCESS","url":null}""", null, null, null)])
        {
            ListedItems = [new Ai79ProviderMediaItem("task-1", "https://cdn.example/recovered.jpg", "SUCCESS", null, null)]
        };
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "recover scene",
            CapabilityConfigJson = """{"poll_max_attempts":1}"""
        });

        Assert.True(result.Success);
        Assert.Equal("https://cdn.example/recovered.jpg", result.ImageUrl);
        Assert.Equal(1, client.ListCalls);
    }

    private static Gommo79AiImageService Create(FakeClient client)
        => new(
            client,
            new StaticCredentialResolver(),
            new ConfigurationBuilder().Build(),
            NullLogger<Gommo79AiImageService>.Instance);

    private sealed class StaticCredentialResolver : IProviderCredentialResolver
    {
        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential
            {
                ProviderAccountId = Guid.Empty,
                ProviderCode = providerCode,
                CredentialRole = credentialRole,
                Secret = "test-secret",
                MaskedHint = "****"
            });
    }

    private sealed class FakeClient : IAi79TaskClient
    {
        private readonly Queue<string> _taskIds;
        private readonly Queue<Ai79TaskStatusResult> _statuses;

        public FakeClient(IEnumerable<string> submitTaskIds, IEnumerable<Ai79TaskStatusResult> statuses)
        {
            _taskIds = new Queue<string>(submitTaskIds);
            _statuses = new Queue<Ai79TaskStatusResult>(statuses);
        }

        public List<Ai79TaskSubmitRequest> Submits { get; } = [];
        public List<string> PolledTaskIds { get; } = [];
        public IReadOnlyList<Ai79ProviderMediaItem> ListedItems { get; init; } = [];
        public int ListCalls { get; private set; }

        public Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default)
        {
            Submits.Add(request);
            return Task.FromResult(new Ai79TaskSubmitResult(_taskIds.Dequeue(), """{"success":true}"""));
        }

        public Task<Ai79TaskStatusResult> GetStatusAsync(Ai79TaskStatusRequest request, CancellationToken ct = default)
        {
            PolledTaskIds.Add(request.TaskId);
            return Task.FromResult(_statuses.Dequeue());
        }

        public Task<Ai79ProviderMediaListResult> ListImagesAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult(new Ai79ProviderMediaListResult(ListedItems, """{"images":[]}"""));
        }

        public Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
