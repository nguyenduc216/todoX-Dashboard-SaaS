using Microsoft.Extensions.Options;
using System.Net;

namespace TodoX.Web.Services.AiProviders.Kie;

public sealed class KieMotionControlBuildRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string CharacterImageUrl { get; set; } = string.Empty;
    public string MotionVideoUrl { get; set; } = string.Empty;
    public string? Mode { get; set; }
    public string? CharacterOrientation { get; set; }
    public string? CallbackUrl { get; set; }
}

public interface IKiePayloadBuilder
{
    KieMotionControlRequest BuildMotionControlRequest(KieMotionControlBuildRequest request);
}

public sealed class KiePayloadBuilder : IKiePayloadBuilder
{
    private readonly IOptionsMonitor<KieOptions> _options;

    public KiePayloadBuilder(IOptionsMonitor<KieOptions> options)
    {
        _options = options;
    }

    public KieMotionControlRequest BuildMotionControlRequest(KieMotionControlBuildRequest request)
    {
        var options = _options.CurrentValue;
        var prompt = (request.Prompt ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new KieProviderException("Prompt is required.", KieErrorCodes.Unknown, transient: false);
        }

        var imageUrl = ValidatePublicHttpsUrl(request.CharacterImageUrl, "input_urls");
        var videoUrl = ValidatePublicHttpsUrl(request.MotionVideoUrl, "video_urls");
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? options.DefaultMode : request.Mode.Trim();
        if (!options.AllowedModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            throw new KieProviderException($"KIE mode '{mode}' is not allowed.", KieErrorCodes.InvalidMode, transient: false);
        }

        var orientation = string.IsNullOrWhiteSpace(request.CharacterOrientation)
            ? "image"
            : request.CharacterOrientation.Trim();
        if (!options.AllowedCharacterOrientations.Contains(orientation, StringComparer.OrdinalIgnoreCase))
        {
            throw new KieProviderException($"KIE character orientation '{orientation}' is not allowed.", KieErrorCodes.InvalidOrientation, transient: false);
        }

        var callback = string.IsNullOrWhiteSpace(request.CallbackUrl)
            ? options.GetCallbackUriOrNull()?.ToString()
            : ValidateCallbackUrl(request.CallbackUrl);

        return new KieMotionControlRequest
        {
            Model = string.IsNullOrWhiteSpace(options.MotionControlModel) ? KieOptions.DefaultModel : options.MotionControlModel.Trim(),
            CallBackUrl = callback,
            Input = new KieMotionControlInput
            {
                Prompt = prompt,
                InputUrls = new List<string> { imageUrl },
                VideoUrls = new List<string> { videoUrl },
                Mode = mode,
                CharacterOrientation = orientation
            }
        };
    }

    public static string ValidatePublicHttpsUrl(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new KieProviderException($"{fieldName} must be an absolute HTTPS URL.", KieErrorCodes.InvalidInputUrl, transient: false);
        }

        if (uri.IsLoopback
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("minio-console", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("/browser/", StringComparison.OrdinalIgnoreCase))
        {
            throw new KieProviderException($"{fieldName} must be a public provider-readable URL.", KieErrorCodes.InvalidInputUrl, transient: false);
        }

        if (IPAddress.TryParse(uri.Host, out var ip) && IsPrivateAddress(ip))
        {
            throw new KieProviderException($"{fieldName} cannot point to a private IP address.", KieErrorCodes.InvalidInputUrl, transient: false);
        }

        return uri.ToString();
    }

    private static string ValidateCallbackUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new KieProviderException("KIE callback URL must be an absolute HTTPS URL.", KieErrorCodes.CallbackInvalid, transient: false);
        }

        return uri.ToString();
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        return true;
    }
}
