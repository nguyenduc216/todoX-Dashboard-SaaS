using System.Net.Mail;
using System.Text.RegularExpressions;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

public static partial class ContactLeadValidator
{
    public static (ContactLeadValidationResult Result, NormalizedContactLead? Lead) Validate(ContactLeadCreateRequest request)
    {
        var result = new ContactLeadValidationResult();
        var fullName = Required(request.FullName);
        if (fullName.Length < 2 || fullName.Length > 200)
        {
            result.Add(nameof(request.FullName), "Họ và tên phải có từ 2 đến 200 ký tự.");
        }

        var phone = NormalizePhone(request.Phone);
        if (phone is null)
        {
            result.Add(nameof(request.Phone), "Số điện thoại Việt Nam không hợp lệ.");
        }

        var email = Optional(request.Email, 320);
        if (email is not null && !IsValidEmail(email))
        {
            result.Add(nameof(request.Email), "Email không hợp lệ.");
        }

        var company = Optional(request.Company, 250, result, nameof(request.Company));
        var industry = Optional(request.Industry, 100, result, nameof(request.Industry));
        var need = Optional(request.Need, 100, result, nameof(request.Need));
        var message = Optional(request.Message, 4000, result, nameof(request.Message));
        var sourceUrl = Optional(request.SourceUrl, 2000, result, nameof(request.SourceUrl));
        var referrerUrl = Optional(request.ReferrerUrl, 2000, result, nameof(request.ReferrerUrl));
        var utmSource = Optional(request.UtmSource, 200, result, nameof(request.UtmSource));
        var utmMedium = Optional(request.UtmMedium, 200, result, nameof(request.UtmMedium));
        var utmCampaign = Optional(request.UtmCampaign, 200, result, nameof(request.UtmCampaign));
        var utmContent = Optional(request.UtmContent, 200, result, nameof(request.UtmContent));
        var utmTerm = Optional(request.UtmTerm, 200, result, nameof(request.UtmTerm));

        if (!request.ConsentAccepted)
        {
            result.Add(nameof(request.ConsentAccepted), "Bạn cần đồng ý chính sách trước khi gửi thông tin.");
        }

        return result.IsValid
            ? (result, new NormalizedContactLead
            {
                FullName = fullName,
                Phone = phone!,
                Email = email,
                Company = company,
                Industry = industry,
                Need = need,
                Message = message,
                SourceUrl = sourceUrl,
                ReferrerUrl = referrerUrl,
                UtmSource = utmSource,
                UtmMedium = utmMedium,
                UtmCampaign = utmCampaign,
                UtmContent = utmContent,
                UtmTerm = utmTerm,
                ConsentAccepted = request.ConsentAccepted
            })
            : (result, null);
    }

    private static string Required(string? value) => (value ?? string.Empty).Trim();

    private static string? Optional(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? Optional(string? value, int maxLength, ContactLeadValidationResult result, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            result.Add(field, $"Tối đa {maxLength} ký tự.");
            return null;
        }

        return trimmed;
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            _ = new MailAddress(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizePhone(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = PhoneSeparators().Replace(raw, string.Empty);
        if (normalized.StartsWith("+84", StringComparison.Ordinal))
        {
            normalized = "0" + normalized[3..];
        }
        else if (normalized.StartsWith("84", StringComparison.Ordinal))
        {
            normalized = "0" + normalized[2..];
        }

        return VietnamesePhone().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex(@"[\s\.\-\(\)]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^0(3|5|7|8|9)\d{8}$")]
    private static partial Regex VietnamesePhone();
}
