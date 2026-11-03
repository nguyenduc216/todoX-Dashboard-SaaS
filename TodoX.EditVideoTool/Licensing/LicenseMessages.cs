using System.Text;
using System.Text.Json;

namespace TodoX.EditVideoTool.Licensing;

/// <summary>Vietnamese messages for the stable error codes returned by /api/editvideo/v1.</summary>
public static class LicenseMessages
{
    public static string ForServerError(string? code, string? serverMessage, JsonElement? detail = null) => code switch
    {
        "invalid_request" => "License key không đúng định dạng (TDXV-XXXXX-XXXXX-XXXXX-XXXXX).",
        "invalid_license" => "License key không tồn tại. Kiểm tra lại key TodoX đã gửi.",
        "license_suspended" => "License đang bị tạm khóa. Liên hệ TodoX.",
        "license_revoked" => "License đã bị thu hồi.",
        "license_expired" => "License đã hết hạn. Liên hệ TodoX để gia hạn.",
        "device_limit_reached" => DeviceLimit(detail),
        "invalid_token" => "Phiên đăng nhập của máy đã hết hiệu lực (máy có thể đã bị gỡ). Hãy kích hoạt lại.",
        "device_mismatch" => "License này đang được dùng trên máy khác.",
        "signing_unavailable" => "Máy chủ license TodoX đang bảo trì. Thử lại sau.",
        LicenseApiClient.NetworkError => serverMessage ?? "Không kết nối được máy chủ TodoX.",
        _ => serverMessage ?? $"Lỗi license ({code})."
    };

    private static string DeviceLimit(JsonElement? detail)
    {
        var sb = new StringBuilder("License đã dùng hết số máy cho phép");
        if (detail is { ValueKind: JsonValueKind.Object } d)
        {
            if (d.TryGetProperty("limit", out var limit))
            {
                var kind = d.TryGetProperty("deviceKind", out var k) && k.GetString() == "mobile" ? "điện thoại" : "máy tính";
                sb.Append($" ({limit} {kind})");
            }

            if (d.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array && devices.GetArrayLength() > 0)
            {
                sb.Append(". Đang dùng trên: ");
                sb.AppendJoin(", ", devices.EnumerateArray().Select(x =>
                    x.TryGetProperty("deviceName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "không rõ tên"));
            }
        }

        return sb.Append(". Gỡ máy cũ trong dashboard TodoX hoặc chạy 'todox-editvideo deactivate' trên máy cũ.").ToString();
    }
}
