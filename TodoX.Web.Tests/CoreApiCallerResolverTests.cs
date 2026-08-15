using TodoX.Web.Services.Platform;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CoreApiCallerResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNoAuthenticatorHandlesRequest()
    {
        var resolver = new CoreApiCallerResolver(Array.Empty<ICoreApiCallerAuthenticator>());
        var http = new DefaultHttpContext();

        var result = await resolver.ResolveAsync(http.Request);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_UsesFirstMatchingAuthenticator()
    {
        var expected = new CoreRequestContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CoreChannelCodes.Zalo,
            "zalo-mini-app",
            "request-1");

        var resolver = new CoreApiCallerResolver(new ICoreApiCallerAuthenticator[]
        {
            new FakeAuthenticator(false, null),
            new FakeAuthenticator(true, expected),
            new FakeAuthenticator(true, new CoreRequestContext(null, null, CoreChannelCodes.Partner))
        });

        var http = new DefaultHttpContext();
        var result = await resolver.ResolveAsync(http.Request);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ResolveAsync_RejectsUnknownChannelReturnedByTransportAdapter()
    {
        var resolver = new CoreApiCallerResolver(new ICoreApiCallerAuthenticator[]
        {
            new FakeAuthenticator(true, new CoreRequestContext(null, null, "unknown-channel"))
        });

        var http = new DefaultHttpContext();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => resolver.ResolveAsync(http.Request));
    }

    private sealed class FakeAuthenticator : ICoreApiCallerAuthenticator
    {
        private readonly bool _canHandle;
        private readonly CoreRequestContext? _context;

        public FakeAuthenticator(bool canHandle, CoreRequestContext? context)
        {
            _canHandle = canHandle;
            _context = context;
        }

        public bool CanHandle(HttpRequest request) => _canHandle;

        public Task<CoreRequestContext?> AuthenticateAsync(HttpRequest request, CancellationToken ct = default)
            => Task.FromResult(_context);
    }
}
