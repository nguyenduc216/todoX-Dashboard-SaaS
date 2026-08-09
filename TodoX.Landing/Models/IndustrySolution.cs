namespace TodoX.Landing.Models;

public sealed class IndustrySolution
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string AspectRatio { get; set; } = "9:16";
    public string? FormatNote { get; set; }
    public string? GoalNote { get; set; }
    public string? CapabilityNote { get; set; }
    public int DisplayOrder { get; set; }
}
