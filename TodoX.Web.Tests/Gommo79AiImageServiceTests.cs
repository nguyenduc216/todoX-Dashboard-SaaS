using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class Gommo79AiImageServiceTests
{
    [Fact]
    public async Task SubmitPassPersistsTaskWithoutPolling()
    {
        var client = new FakeClient(["task-1"], []);
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "construction scene",
            AspectRatio = "16:9",
            ReferenceImageBase64 = "data:image/jpeg;base64,AAAA"
        });

        Assert.False(result.Success);
        Assert.Contains("task-1", result.UsageJson);
        Assert.Empty(client.PolledTaskIds);
        Assert.Equal("16_9", client.Submits.Single().Options["ratio"]);
        Assert.Equal("true", client.Submits.Single().Options["editImage"]);
        Assert.StartsWith("data:image/jpeg;base64,", client.Submits.Single().Options["base64Image"]);
    }

    [Fact]
    public async Task PollPassUsesPersistedTaskIdAndCompletes()
    {
        var client = new FakeClient(["unused"],
            [new Ai79TaskStatusResult("SUCCESS", """{"status":"SUCCESS","url":"https://cdn.example/two.jpg"}""",
                "https://cdn.example/two.jpg", null, null)]);
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "scene",
            Model = "google_image_gen_banana_2",
            ProviderTaskId = "task-1"
        });

        Assert.True(result.Success);
        Assert.Empty(client.Submits);
        Assert.Equal(["task-1"], client.PolledTaskIds);
    }

    [Fact]
    public async Task PendingPollDoesNotFallback()
    {
        var client = new FakeClient(["unused"],
            [new Ai79TaskStatusResult("RUNNING", """{"status":"PENDING_PROCESSING"}""", null, null, null)]);
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "long scene",
            ProviderTaskId = "task-1"
        });

        Assert.False(result.Success);
        Assert.Contains("task-1", result.UsageJson);
        Assert.Empty(client.Submits);
    }

    [Fact]
    public async Task RequestedFallbackModelControlsProviderPayload()
    {
        var client = new FakeClient(["task-fallback"], []);
        var service = Create(client);

        await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "fallback scene",
            Model = "google_image_gen_banana_2",
            RequestedModel = "seedream_4_5"
        });

        var submit = Assert.Single(client.Submits);
        Assert.Equal("seedream_4_5", submit.Model);
        Assert.Equal("vip", submit.Options["mode"]);
        Assert.Equal("2k", submit.Options["resolution"]);
    }

    [Fact]
    public async Task ImageRecoveryRequiresExactTaskId()
    {
        var client = new FakeClient(["unused"],
            [new Ai79TaskStatusResult("SUCCESS", """{"status":"SUCCESS","url":null}""", null, null, null)])
        {
            ListedItems =
            [
                new Ai79ProviderMediaItem("other", "https://cdn.example/wrong.jpg", "SUCCESS", null, null),
                new Ai79ProviderMediaItem("task-1", "https://cdn.example/right.jpg", "SUCCESS", null, null)
            ]
        };
        var service = Create(client);

        var result = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Prompt = "recover scene",
            ProviderTaskId = "task-1"
        });

        Assert.True(result.Success);
        Assert.Equal("https://cdn.example/right.jpg", result.ImageUrl);
    }

    private static Gommo79AiImageService Create(FakeClient client)
        => new(client, new StaticCredentialResolver(), new ConfigurationBuilder().Build(),
            NullLogger<Gommo79AiImageService>.Instance);

    private sealed class StaticCredentialResolver : IProviderCredentialResolver
    {
        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential
            {
                ProviderAccountId = Guid.Empty, ProviderCode = providerCode,
                CredentialRole = credentialRole, Secret = "test-secret", MaskedHint = "****"
            });
    }

    private sealed class FakeClient : IAi79TaskClient
    {
        private readonly Queue<string> _taskIds;
        private readonly Queue<Ai79TaskStatusResult> _statuses;
        public FakeClient(IEnumerable<string> taskIds, IEnumerable<Ai79TaskStatusResult> statuses)
        {
            _taskIds = new(taskIds);
            _statuses = new(statuses);
        }
        public List<Ai79TaskSubmitRequest> Submits { get; } = [];
        public List<string> PolledTaskIds { get; } = [];
        public IReadOnlyList<Ai79ProviderMediaItem> ListedItems { get; init; } = [];
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
            => Task.FromResult(new Ai79ProviderMediaListResult(ListedItems, """{"images":[]}"""));
        public Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
