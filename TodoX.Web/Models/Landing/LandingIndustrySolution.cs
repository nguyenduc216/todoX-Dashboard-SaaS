namespace TodoX.Web.Models.Landing;

public static class LandingIndustryPermissions
{
    public const string View = "landing.industries.view";
    public const string Create = "landing.industries.create";
    public const string Update = "landing.industries.update";
    public const string Delete = "landing.industries.delete";
}

public static class LandingIndustryAspectRatios
{
    public const string Portrait = "9:16";
    public const string Landscape = "16:9";

    public static readonly string[] All = [Portrait, Landscape];

    public static bool IsValid(string? value) => All.Contains(value);
}

public sealed class LandingIndustrySolution
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string AspectRatio { get; set; } = LandingIndustryAspectRatios.Portrait;
    public string? FormatNote { get; set; }
    public string? GoalNote { get; set; }
    public string? CapabilityNote { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public bool IsDeleted => DeletedAt is not null;
}

public sealed class LandingIndustrySolutionEdit
{
    public Guid? Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string AspectRatio { get; set; } = LandingIndustryAspectRatios.Portrait;
    public string? FormatNote { get; set; }
    public string? GoalNote { get; set; }
    public string? CapabilityNote { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class LandingIndustryVideo
{
    public Guid Id { get; set; }
    public Guid IndustrySolutionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string AspectRatio { get; set; } = LandingIndustryAspectRatios.Portrait;
    public string? FormatNote { get; set; }
    public string? GoalNote { get; set; }
    public string? CapabilityNote { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public bool IsDeleted => DeletedAt is not null;
}

public sealed class LandingIndustryVideoEdit
{
    public Guid? Id { get; set; }
    public Guid IndustrySolutionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string AspectRatio { get; set; } = LandingIndustryAspectRatios.Portrait;
    public string? FormatNote { get; set; }
    public string? GoalNote { get; set; }
    public string? CapabilityNote { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class LandingIndustrySolutionValidationException : Exception
{
    public LandingIndustrySolutionValidationException(string message)
        : base(message)
    {
    }
}

public sealed class LandingIndustrySolutionDuplicateSlugException : Exception
{
    public LandingIndustrySolutionDuplicateSlugException(string slug)
        : base($"Slug '{slug}' đã tồn tại.")
    {
    }
}

public sealed class LandingIndustrySchemaUnavailableException : Exception
{
    public LandingIndustrySchemaUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
