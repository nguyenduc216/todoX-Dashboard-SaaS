using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TodoX.Web.Services;

public sealed class FacebookSignedRequestService
{
    public JsonDocument? ParseAndValidate(string signedRequest, string appSecret)
    {
        if (string.IsNullOrWhiteSpace(signedRequest) || string.IsNullOrWhiteSpace(appSecret))
        {
            return null;
        }

        var parts = signedRequest.Split('.', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        var encodedSig = parts[0];
        var encodedPayload = parts[1];

        var sig = Base64UrlDecode(encodedSig);
        var payloadBytes = Encoding.UTF8.GetBytes(encodedPayload);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var expectedSig = hmac.ComputeHash(payloadBytes);

        if (!CryptographicOperations.FixedTimeEquals(sig, expectedSig))
        {
            return null;
        }

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(encodedPayload));
        return JsonDocument.Parse(payloadJson);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }
}
