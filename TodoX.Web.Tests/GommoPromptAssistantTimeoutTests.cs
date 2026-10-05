using System.Net;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class GommoPromptAssistantTimeoutTests
{
    private static ServicePromptAssistantOptions CreateOptions(
        int connectSeconds = 5,
        int idleSeconds = 5,
        int hardSeconds = 30)
        => new()
        {
            ConnectTimeoutSeconds = connectSeconds,
            StreamIdleTimeoutSeconds = idleSeconds,
            StreamHardTimeoutSeconds = hardSeconds
        };

    private static ServicePrompt79AiClient CreateClient(HttpMessageHandler handler, ServicePromptAssistantOptions options)
        => new(new HttpClient(handler), new StaticResolver("secret-token"), Options.Create(options));

    private static ServicePrompt79AiClient CreateClient(HttpMessageHandler handler, ServicePromptAssistantOptions options, IProviderCredentialResolver resolver)
        => new(new HttpClient(handler), resolver, Options.Create(options));

    private static ServicePromptProviderRequest CreateRequest()
        => new("https://api.gommo.net/api/v2/chat", "gommo_agent", "base-123", "make demo");

    private static string Chunk(string content) => System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content } } } });

    [Fact]
    public async Task FastStreamWithinAllTimeoutsSucceeds()
    {
        var handler = new StubHandler(
            $"data: {Chunk("{\"scene\":\"demo\"}")}\n\n" +
            "data: [DONE]\n\n");
        var client = CreateClient(handler, CreateOptions());

        var result = await client.CompleteAsync(CreateRequest());

        Assert.Equal("{\"scene\":\"demo\"}", result.Content);
        Assert.True(result.Diagnostics?.DoneReceived);
    }

    [Fact]
    public async Task ConnectTimeoutBeforeHeadersYieldsGommoConnectTimeout()
    {
        var handler = new StubHandler("", delayBeforeHeaders: TimeSpan.FromSeconds(2));
        var client = CreateClient(handler, CreateOptions(connectSeconds: 1));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_connect_timeout", error.Code);
    }

    [Fact]
    public async Task IdleTimeoutWhenNoSseLineArrivesYieldsGommoStreamIdleTimeout()
    {
        // Headers arrive immediately, one chunk arrives, then the stream stalls (no more lines).
        var handler = new StubHandlerWithStalledStream($"data: {Chunk("{\"a\":1}")}\n\n");
        var client = CreateClient(handler, CreateOptions(connectSeconds: 5, idleSeconds: 1, hardSeconds: 30));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_idle_timeout", error.Code);
        Assert.Equal("idle", error.Diagnostics?.TimeoutStage);
        Assert.NotNull(error.Diagnostics?.ElapsedMsSinceLastSseEvent);
    }

    [Fact]
    public async Task IdleTimerResetsOnEachArrivingLine()
    {
        // Gaps of 150ms each are below the 1s idle timeout; stream completes.
        var handler = new StubHandlerWithDelayedLines(
            new[]
            {
                $"data: {Chunk("{\"a\":")}",
                $"data: {Chunk("1}")}",
                "data: [DONE]"
            },
            interLineDelay: TimeSpan.FromMilliseconds(150));
        var client = CreateClient(handler, CreateOptions(connectSeconds: 5, idleSeconds: 1, hardSeconds: 30));

        var result = await client.CompleteAsync(CreateRequest());

        Assert.Equal("{\"a\":1}", result.Content);
        Assert.True(result.Diagnostics?.DoneReceived);
    }

    [Fact]
    public async Task HardTimeoutWithContinuousChunksAndNoDoneYieldsGommoStreamHardTimeout()
    {
        var handler = new StubHandlerInfiniteChunkStream(interChunkDelay: TimeSpan.FromMilliseconds(50));
        var client = CreateClient(handler, CreateOptions(connectSeconds: 5, idleSeconds: 5, hardSeconds: 1));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_hard_timeout", error.Code);
        Assert.Equal("hard", error.Diagnostics?.TimeoutStage);
        Assert.True(error.Diagnostics?.SseDataCount > 0);
        Assert.False(error.Diagnostics?.DoneReceived);
    }

    [Fact]
    public async Task HealthyLongStreamSurvivesLegacyTotalTimeoutWindow()
    {
        // Regression for the production incident: first event immediate, chunks keep
        // arriving within the idle window, no DONE for longer than the legacy total
        // timeout (1s), and the stream finally completes before the hard timeout.
        // The legacy single absolute timeout would have killed this stream at 1s.
        var lines = new List<string> { $"data: {Chunk("{\"a\":")}" };
        for (var i = 0; i < 20; i++) lines.Add($"data: {Chunk("x")}");
        lines.Add($"data: {Chunk("1}")}");
        lines.Add("data: [DONE]");
        var handler = new StubHandlerWithDelayedLines(lines.ToArray(), interLineDelay: TimeSpan.FromMilliseconds(60));
        var options = CreateOptions(connectSeconds: 2, idleSeconds: 2, hardSeconds: 10);
        options.TimeoutSeconds = 1; // legacy absolute timeout that would previously kill the stream
        var client = CreateClient(handler, options);

        var result = await client.CompleteAsync(CreateRequest());

        Assert.Equal("{\"a\":xxxxxxxxxxxxxxxxxxxx1}", result.Content);
        Assert.True(result.Diagnostics?.DoneReceived);
        Assert.True(result.TotalDurationMs > 1000); // survived beyond the legacy 1s limit
    }

    [Fact]
    public async Task ExternalCancellationIsNotConvertedToProviderTimeout()
    {
        var handler = new StubHandlerInfiniteChunkStream(interChunkDelay: TimeSpan.FromMilliseconds(50));
        var client = CreateClient(handler, CreateOptions());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(CreateRequest(), cts.Token));
    }

    [Fact]
    public async Task MalformedSseChunkStillFailsWithMalformedSseChunk()
    {
        var handler = new StubHandler("data: {\"invalid\":\"secret-token\"\n\n");
        var client = CreateClient(handler, CreateOptions());

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("malformed_sse_chunk", error.Code);
    }

    [Fact]
    public async Task CredentialTimeoutUsesConnectTimeoutTaxonomy()
    {
        var handler = new StubHandler($"data: {Chunk("{}")}\n\ndata: [DONE]\n\n");
        var client = CreateClient(handler, CreateOptions(connectSeconds: 1), new DelayedResolver(TimeSpan.FromSeconds(2)));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_connect_timeout", error.Code);
    }

    [Fact]
    public async Task NoSseEventTimeoutDiagnosticIsNull()
    {
        // Stream opens (headers) but emits no data: line and stalls -> idle timeout with no SSE data event.
        var handler = new StubHandlerWithStalledStream("");
        var client = CreateClient(handler, CreateOptions(connectSeconds: 5, idleSeconds: 1, hardSeconds: 30));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_idle_timeout", error.Code);
        Assert.Null(error.Diagnostics?.ElapsedMsSinceLastSseEvent);
    }

    [Fact]
    public async Task IdleTimeoutElapsedSinceLastSseEventReflectsLastEventNotFirst()
    {
        // Event at t1 (fast), event at t2 (after ~400ms), stall, idle timeout fires 1s after t2.
        // ElapsedMsSinceLastSseEvent must be ~idle timeout (~1000ms), NOT ~1400ms (since first event).
        var handler = new StubHandlerWithDelayedLinesThenStall(
            new[] { $"data: {Chunk("a")}", $"data: {Chunk("b")}" },
            interLineDelay: TimeSpan.FromMilliseconds(400));
        var client = CreateClient(handler, CreateOptions(connectSeconds: 5, idleSeconds: 1, hardSeconds: 30));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_idle_timeout", error.Code);
        Assert.Equal("idle", error.Diagnostics?.TimeoutStage);
        var elapsed = error.Diagnostics?.ElapsedMsSinceLastSseEvent;
        Assert.NotNull(elapsed);
        // Close to the 1s idle window measured from the LAST event; with tolerance since
        // first event would give ~1400ms and the last-event gap gives ~1000ms.
        Assert.InRange(elapsed!.Value, 700, 1200);
    }

    [Fact]
    public async Task HardTimeoutElapsedSinceLastSseEventReflectsRecentChunkGap()
    {
        // Chunks arrive continuously every 60ms; hard timeout fires at 1s.
        // ElapsedSinceLastSseEvent should be the small gap since last chunk (< total duration).
        var handler = new StubHandlerInfiniteChunkStream(interChunkDelay: TimeSpan.FromMilliseconds(60));
        var client = CreateClient(handler, CreateOptions(connectSeconds: 5, idleSeconds: 5, hardSeconds: 1));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_hard_timeout", error.Code);
        Assert.Equal("hard", error.Diagnostics?.TimeoutStage);
        var elapsed = error.Diagnostics?.ElapsedMsSinceLastSseEvent;
        Assert.NotNull(elapsed);
        Assert.True(elapsed!.Value < error.Diagnostics!.TotalDurationMs, $"elapsed {elapsed} should be < total {error.Diagnostics.TotalDurationMs}");
        Assert.InRange(elapsed!.Value, 0, 400); // recent chunk gap, not stream age
    }

    private sealed class StaticResolver(string secret) : IProviderCredentialResolver
    {
        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential { ProviderAccountId = Guid.NewGuid(), ProviderCode = providerCode, CredentialRole = credentialRole, Secret = secret });
    }

    private sealed class DelayedResolver(TimeSpan delay) : IProviderCredentialResolver
    {
        public async Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class StubHandler(string body, TimeSpan? delayBeforeHeaders = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (delayBeforeHeaders is { } delay)
                await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream") };
        }
    }

    private sealed class StubHandlerWithStalledStream(string initialLines) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StalledSseStream(initialLines))
                { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") } }
            });
    }

    private sealed class StubHandlerWithDelayedLines(string[] lines, TimeSpan interLineDelay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new DelayedLinesSseStream(lines, interLineDelay))
                { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") } }
            });
    }

    /// <summary>Emits the given SSE lines with delay between them, then stalls (no more data, stream stays open).</summary>
    private sealed class StubHandlerWithDelayedLinesThenStall(string[] lines, TimeSpan interLineDelay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new DelayedThenStalledSseStream(lines, interLineDelay))
                { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") } }
            });
    }

    private sealed class StubHandlerInfiniteChunkStream(TimeSpan interChunkDelay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InfiniteChunkSseStream(interChunkDelay))
                { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") } }
            });
    }

    /// <summary>Emits initial SSE lines, then stays open with no data (reader blocks until cancelled).</summary>
    private sealed class StalledSseStream(string initialLines) : Stream
    {
        private readonly byte[] _initial = System.Text.Encoding.UTF8.GetBytes(initialLines);
        private int _position;
        private bool _stalled;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_stalled)
            {
                var remaining = _initial.Length - _position;
                if (remaining > 0)
                {
                    var take = Math.Min(count, remaining);
                    Array.Copy(_initial, _position, buffer, offset, take);
                    _position += take;
                    if (_position >= _initial.Length) _stalled = true;
                    return take;
                }
                _stalled = true;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Emits the given SSE lines with a delay before each line.</summary>
    private sealed class DelayedLinesSseStream(string[] lines, TimeSpan delay) : Stream
    {
        private int _lineIndex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_lineIndex >= lines.Length) return 0;
            await Task.Delay(delay, cancellationToken);
            var bytes = System.Text.Encoding.UTF8.GetBytes(lines[_lineIndex] + "\n\n");
            var take = Math.Min(count, bytes.Length);
            Array.Copy(bytes, 0, buffer, offset, take);
            _lineIndex++;
            return take;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Emits the given lines with delay before each, then stalls forever (read blocks until cancelled).</summary>
    private sealed class DelayedThenStalledSseStream(string[] lines, TimeSpan delay) : Stream
    {
        private int _lineIndex;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_lineIndex >= lines.Length)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0; // unreachable
            }
            await Task.Delay(delay, cancellationToken);
            var bytes = System.Text.Encoding.UTF8.GetBytes(lines[_lineIndex] + "\n\n");
            _lineIndex++;
            var take = Math.Min(count, bytes.Length);
            Array.Copy(bytes, 0, buffer, offset, take);
            return take;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Emits SSE content chunks forever (no [DONE]) with the given delay between chunks.</summary>
    private sealed class InfiniteChunkSseStream(TimeSpan delay) : Stream
    {
        private int _emitted;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            var bytes = System.Text.Encoding.UTF8.GetBytes("data: " + Chunk($"\"part-{_emitted}\"") + "\n\n");
            _emitted++;
            var take = Math.Min(count, bytes.Length);
            Array.Copy(bytes, 0, buffer, offset, take);
            return take;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
