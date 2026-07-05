namespace TodoX.Web.Models;

public sealed class ReferenceVideoDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public string Platform { get; set; } = "unknown";
    public string SourceUrl { get; set; } = string.Empty;
    public string? NormalizedUrl { get; set; }
    public string? ExternalVideoId { get; set; }

    public string? ChannelName { get; set; }
    public string? ChannelUrl { get; set; }
    public string? AuthorHandle { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string[] Hashtags { get; set; } = Array.Empty<string>();
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ThumbnailUrl { get; set; }

    public string Status { get; set; } = "new";
    public Guid? ReupJobId { get; set; }
    public string AddedFrom { get; set; } = "chrome_extension";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ReferenceVideoCreateRequest
{
    public string Platform { get; set; } = "unknown";
    public string SourceUrl { get; set; } = string.Empty;

    public string? ChannelName { get; set; }
    public string? ChannelUrl { get; set; }
    public string? AuthorHandle { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string[]? Hashtags { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ThumbnailUrl { get; set; }

    public string? ExternalVideoId { get; set; }
    public object? RawMetadata { get; set; }
}

public sealed class ReferenceVideoFilter
{
    public Guid CustomerId { get; set; }
    public string? Platform { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int Limit { get; set; } = 100;
    public int Offset { get; set; }
}

public sealed class ExtensionMeResponse
{
    public Guid CustomerId { get; set; }
    public Guid UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? UserEmail { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ExtensionTokenValidationResult
{
    public bool IsValid { get; set; }
    public Guid CustomerId { get; set; }
    public Guid UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? UserEmail { get; set; }
}

public sealed record ExtensionIssueResult(string RawToken, string TokenPrefix);

public sealed record ExtensionPackageResult(string FileName, string ContentType, byte[] Bytes);
