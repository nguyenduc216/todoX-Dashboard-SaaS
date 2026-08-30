using System.Net;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class VbeeVoiceClientTests
{
    [Fact]
    public async Task SubmitAsync_ParsesRootRequestId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"request_id":"req-123"}""")
        });
        var client = CreateClient(handler, new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var result = await client.SubmitAsync(new VbeeVoiceSubmitRequest(
            "voice-01",
            "Xin chao",
            1.0m,
            null,
            "https://dashboard.example.com/api/providers/vbee/callback",
            "logical-request-1",
            0,
            160,
            1.25m,
            null));

        Assert.Equal("req-123", result.RequestId);
        Assert.Equal(200, result.Response!["http_status"]!.GetValue<int>());
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal("https://vbee.example/api/v1/tts", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("token-123", handler.LastRequest?.Headers.Authorization?.Parameter);

        var body = handler.LastBody ?? string.Empty;
        Assert.Contains("\"app_id\":\"app-456\"", body);
        Assert.DoesNotContain("callback_url", body);
        Assert.DoesNotContain("logical-request-1", body);
        Assert.DoesNotContain("request_id", body);
        Assert.Contains("\"input_text\":\"Xin chao\"", body);
        Assert.Contains("\"voice_code\":\"voice-01\"", body);
        Assert.Contains("\"audio_type\":\"mp3\"", body);
        Assert.Contains("\"bitrate\":160", body);
        Assert.Contains("\"speed_rate\":1.25", body);
        Assert.DoesNotContain("sample_rate", body);
    }

    [Fact]
    public async Task SubmitAsync_ParsesNestedRequestId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"result":{"request_id":"req-2"}}""")
        });
        var client = CreateClient(handler, new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var result = await client.SubmitAsync(new VbeeVoiceSubmitRequest(
            "voice-01",
            "Xin chao",
            1.0m,
            null,
            "https://dashboard.example.com/api/providers/vbee/callback",
            null,
            0,
            160,
            1.25m,
            null));

        Assert.Equal("req-2", result.RequestId);
    }

    [Fact]
    public async Task SubmitAsync_ParsesNestedCamelRequestId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"requestId":"req-3"}}""")
        });
        var client = CreateClient(handler, new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var result = await client.SubmitAsync(new VbeeVoiceSubmitRequest(
            "voice-01",
            "Xin chao",
            1.0m,
            null,
            "https://dashboard.example.com/api/providers/vbee/callback",
            null,
            0,
            160,
            1.25m,
            null));

        Assert.Equal("req-3", result.RequestId);
    }

    [Fact]
    public async Task SubmitAsync_ParsesNestedAudioUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"result":{"audio_link":"https://cdn.example.com/out.mp3"}}""")
        });
        var client = CreateClient(handler, new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var result = await client.SubmitAsync(new VbeeVoiceSubmitRequest(
            "voice-01",
            "Xin chao",
            1.0m,
            null,
            "https://dashboard.example.com/api/providers/vbee/callback",
            null,
            0,
            160,
            1.25m,
            null));

        Assert.Equal("https://cdn.example.com/out.mp3", result.AudioUrl);
    }

    [Fact]
    public async Task SubmitAsync_Non2xxThrowsSafeSubmitException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"status":"FAILED","error_code":"bad_request","error_message":"Provider rejected"}""")
        });
        var client = CreateClient(handler, new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var ex = await Assert.ThrowsAsync<VbeeVoiceSubmitException>(() => client.SubmitAsync(new VbeeVoiceSubmitRequest(
            "voice-01",
            "Xin chao",
            1.0m,
            null,
            "https://dashboard.example.com/api/providers/vbee/callback",
            null,
            0,
            160,
            1.25m,
            null)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
        Assert.Equal("bad_request", ex.ErrorCode);
        Assert.Equal("Provider rejected", ex.ErrorMessage);
        Assert.Contains("status", ex.ResponseTopLevelKeys);
        var shapeKeys = Assert.IsType<JsonArray>(ex.ResponseShape["keys"]);
        Assert.Contains("error_code", shapeKeys.Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task GetStatusAsync_UsesCallbackResultEndpointAndBearerToken()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"SUCCESS","audio_link":"https://cdn.example.com/out.mp3"}""")
        });
        var client = CreateClient(handler, new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var result = await client.GetStatusAsync("req-123");

        Assert.Equal("SUCCESS", result["status"]?.GetValue<string>());
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.Equal("https://vbee.example/api/v1/tts/req-123/callback-result", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("token-123", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ParseCallbackPayloadAsync_ReadsRequestIdAndAudioUrl()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), new VbeeOptions
        {
            ApiBaseUrl = "https://vbee.example/api/v1",
            TtsPath = "/tts",
            ApiToken = "token-123",
            AppId = "app-456"
        });

        var result = await client.ParseCallbackPayloadAsync("""
        {"request_id":"req-abc","scene_id":42,"status":"SUCCESS","audio_url":"https://cdn.example.com/callback.mp3"}
        """);

        Assert.Equal("req-abc", result.RequestId);
        Assert.Equal(42, result.SceneId);
        Assert.Equal("https://cdn.example.com/callback.mp3", result.AudioUrl);
        Assert.Equal("SUCCESS", result.Status);
    }

    [Fact]
    public void CallbackUrl_AppendsSecretWithoutDroppingExistingQueryOrDuplicatingIt()
    {
        var options = new VbeeOptions
        {
            CallbackUrl = "https://dashboard.example.com/api/providers/vbee/callback?tenant=demo",
            CallbackSecret = "secret value"
        };

        var uri = options.GetCallbackUriOrNull()!;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty);

        Assert.Equal("demo", query["tenant"]);
        Assert.Equal("secret value", query["secret"]);
        Assert.Equal(1, query.Keys.Count(x => string.Equals(x, "secret", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CallbackUrl_DoesNotRequireSecretForLegacyOptionalUrl()
    {
        var options = new VbeeOptions
        {
            CallbackUrl = "https://dashboard.example.com/api/providers/vbee/callback?tenant=demo"
        };

        var uri = options.GetCallbackUriOrNull()!;

        Assert.Equal("https://dashboard.example.com/api/providers/vbee/callback?tenant=demo", uri.ToString());
    }

    private static VbeeVoiceClient CreateClient(FakeHttpMessageHandler handler, VbeeOptions options)
        => new(new HttpClient(handler), new StaticOptionsMonitor<VbeeOptions>(options));

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _handler(request, cancellationToken);
        }
    }
}
