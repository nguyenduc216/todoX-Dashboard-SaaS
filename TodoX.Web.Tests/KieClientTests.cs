using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiProviders.Kie;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class KieClientTests
{
    [Fact]
    public async Task CreateTaskAsync_PostsToExpectedEndpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":200,"data":{"taskId":"task-123"}}""")
        });
        var client = CreateClient(handler);

        var result = await client.CreateTaskAsync(new KieMotionControlRequest
        {
            Model = "kling-2.6/motion-control",
            Input = new KieMotionControlInput
            {
                Prompt = "Dance.",
                InputUrls = new List<string> { "https://cdn.example.com/character.png" },
                VideoUrls = new List<string> { "https://cdn.example.com/motion.mp4" },
                Mode = "720p",
                CharacterOrientation = "image"
            }
        }, CancellationToken.None);

        Assert.Equal("task-123", result.TaskId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal("https://api.kie.ai/api/v1/jobs/createTask", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task GetTaskDetailAsync_GetsExpectedEndpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":200,"data":{"taskId":"task-123","state":"generating"}}""")
        });
        var client = CreateClient(handler);

        var result = await client.GetTaskDetailAsync("task-123", CancellationToken.None);

        Assert.Equal(KieTaskStatuses.Rendering, result.Status);
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.Equal("https://api.kie.ai/api/v1/jobs/recordInfo?taskId=task-123", handler.LastRequest?.RequestUri?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, KieErrorCodes.SubmitBadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, KieErrorCodes.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, KieErrorCodes.Forbidden, false)]
    [InlineData(HttpStatusCode.TooManyRequests, KieErrorCodes.RateLimited, true)]
    [InlineData(HttpStatusCode.InternalServerError, KieErrorCodes.ProviderUnavailable, true)]
    public async Task CreateTaskAsync_MapsHttpErrors(HttpStatusCode statusCode, string errorCode, bool transient)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("""{"error":"nope"}""")
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<KieProviderException>(() => client.CreateTaskAsync(new KieMotionControlRequest
        {
            Model = "kling-2.6/motion-control",
            Input = new KieMotionControlInput { Prompt = "Dance." }
        }, CancellationToken.None));

        Assert.Equal(errorCode, ex.ErrorCode);
        Assert.Equal(transient, ex.IsTransient);
        Assert.Contains("nope", ex.RawResponse);
    }

    private static KieClient CreateClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new StaticOptionsMonitor<KieOptions>(new KieOptions { ApiKey = "test-key" }),
            NullLogger<KieClient>.Instance);

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }
}
