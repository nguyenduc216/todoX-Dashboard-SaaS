using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Tests;

internal sealed class FakeDevice : IDeviceIdentity
{
    public string DeviceHash { get; init; } = new('a', 64);
    public string DeviceName { get; init; } = "TEST-PC";
    public string Os { get; init; } = "Windows test";
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Signs license documents exactly like TodoX.Web's EditVideoLicenseSigner.</summary>
internal sealed class TestSigner : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public LicenseVerifier Verifier => new(_key.ExportSubjectPublicKeyInfoPem());

    public string Sign(LicensePayload payload) => SignRaw(JsonSerializer.SerializeToUtf8Bytes(payload, Json));

    public string SignRaw(byte[] body) => $"{B64(body)}.{B64(_key.SignData(body, HashAlgorithmName.SHA256))}";

    public void Dispose() => _key.Dispose();

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Records requests and replies with queued JSON bodies (or throws to simulate no network).</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public Queue<(HttpStatusCode Status, string? Body)> Replies { get; } = new();
    public List<(string Path, string? Auth, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        var (status, body) = Replies.Dequeue();
        if (body is null)
        {
            throw new HttpRequestException("offline");
        }

        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
