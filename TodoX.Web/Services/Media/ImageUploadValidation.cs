namespace TodoX.Web.Services.Media;

public static class ImageUploadValidation
{
    public const string InvalidImageMessage = "Chỉ chấp nhận ảnh PNG, JPEG, WEBP.";

    public static string Validate(byte[] content, string? fileName, string? contentType, long maxBytes)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("File ảnh đang rỗng.");
        if (content.Length > maxBytes)
            throw new InvalidOperationException($"File ảnh tối đa {Math.Max(1, maxBytes / 1024 / 1024)}MB.");

        var extensionMime = GetMimeTypeFromFileName(fileName);
        var sniffedMime = DetectMime(content);
        if (extensionMime is null || sniffedMime is null || !string.Equals(extensionMime, sniffedMime, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(InvalidImageMessage);
        }

        return sniffedMime;
    }

    public static string? DetectMime(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8
            && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";

        if (content.Length >= 3
            && content[..3].SequenceEqual(new byte[] { 255, 216, 255 }))
            return "image/jpeg";

        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";

        return null;
    }

    public static string? GetMimeTypeFromFileName(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
    }
}
