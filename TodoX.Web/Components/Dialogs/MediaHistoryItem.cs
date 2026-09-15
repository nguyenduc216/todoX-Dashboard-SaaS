namespace TodoX.Web.Components.Dialogs;

public sealed class MediaHistoryItem
{
    public string Key { get; init; } = string.Empty;
    public string VersionLabel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public MudBlazor.Color StatusColor { get; init; } = MudBlazor.Color.Default;
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsSelected { get; init; }
    public bool CanSelect { get; init; }
    public bool IsVideo { get; init; }
    public string? MediaUrl { get; init; }
    public string? Metadata { get; init; }
    public string? Prompt { get; init; }
    public string? Error { get; init; }
}
