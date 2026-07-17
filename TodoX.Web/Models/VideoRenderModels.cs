namespace TodoX.Web.Models;

public static class VideoProjectStatuses
{
    public const string Draft = "draft";
    public const string SceneReady = "scene_ready";
    public const string Rendering = "rendering";
    public const string ReadyToMerge = "ready_to_merge";
    public const string Merging = "merging";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class VideoSceneStatuses
{
    public const string Draft = "draft";
    public const string ImageReady = "image_ready";
    public const string VideoQueued = "video_queued";
    public const string VideoRendering = "video_rendering";
    public const string VideoReady = "video_ready";
    public const string Failed = "failed";
}

public enum ScenePromptKind
{
    Image,
    Motion,
    Voice
}

public sealed class VideoProjectDto
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Title { get; set; }
    public string OriginalPrompt { get; set; } = string.Empty;
    public int TotalSeconds { get; set; }
    public int SceneSeconds { get; set; }
    public int SceneCount { get; set; }
    public bool ThinkScenes { get; set; }
    public long? CharacterId { get; set; }
    public string? UploadedCharacterUrl { get; set; }
    public string StorageRoot { get; set; } = string.Empty;
    public string PublicBase { get; set; } = string.Empty;
    public string JobFolder { get; set; } = string.Empty;
    public string Status { get; set; } = VideoProjectStatuses.Draft;
    public string? FinalVideoUrl { get; set; }
    public string? FinalVideoPath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<VideoProjectSceneDto> Scenes { get; set; } = new();
    public List<VideoProjectEventDto> Events { get; set; } = new();
}

public sealed class VideoProjectSceneDto
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public int SceneIndex { get; set; }
    public string? Title { get; set; }
    public int DurationSeconds { get; set; }
    public string ScenePrompt { get; set; } = string.Empty;
    public string? ImagePrompt { get; set; }
    public string? VideoPrompt { get; set; }
    public string? StaticImagePath { get; set; }
    public string? StaticImageUrl { get; set; }
    public string? SceneVideoPath { get; set; }
    public string? SceneVideoUrl { get; set; }
    public string Status { get; set; } = VideoSceneStatuses.Draft;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class VideoProjectEventDto
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class VideoProjectListItemDto
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Title { get; set; }
    public string OriginalPrompt { get; set; } = string.Empty;
    public string? AspectRatio { get; set; }
    public long? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public int SceneCount { get; set; }
    public int ImageReadyCount { get; set; }
    public int VideoReadyCount { get; set; }
    public string Status { get; set; } = VideoProjectStatuses.Draft;
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class VideoProjectCreateRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public long? CharacterId { get; set; }
    public string? UploadedCharacterUrl { get; set; }
    public bool ThinkScenes { get; set; } = true;
    public int TotalSeconds { get; set; } = 16;
    public int SceneSeconds { get; set; } = 8;
    public string? Title { get; set; }
    public bool CreateEmptyProject { get; set; }
    public List<VideoProjectSceneCreateRequest> Scenes { get; set; } = new();
}

public sealed class VideoProjectSceneCreateRequest
{
    public int SceneIndex { get; set; }
    public string? Title { get; set; }
    public int DurationSeconds { get; set; }
    public string ScenePrompt { get; set; } = string.Empty;
    public string? ImagePrompt { get; set; }
    public string? VideoPrompt { get; set; }
}

public sealed class VideoProjectSaveSceneRequest
{
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public string ScenePrompt { get; set; } = string.Empty;
    public string? ImagePrompt { get; set; }
    public string? VideoPrompt { get; set; }
}

public sealed class VideoProjectSaveSceneDraftRequest
{
    public long SceneId { get; set; }
    public string Status { get; set; } = VideoSceneStatuses.Draft;
    public string? Title { get; set; }
    public int SceneIndex { get; set; }
    public string ScenePrompt { get; set; } = string.Empty;
    public string? ImagePrompt { get; set; }
    public string? VideoPrompt { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImagePath { get; set; }
    public string? VideoUrl { get; set; }
    public string? VideoPath { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? SelectedImageVersionId { get; set; }
    public Guid? SelectedVideoVersionId { get; set; }
    public string? AspectRatio { get; set; }
}

public sealed class VideoProjectUpdateRequest
{
    public string? Title { get; set; }
    public string OriginalPrompt { get; set; } = string.Empty;
    public long? CharacterId { get; set; }
    public int TotalSeconds { get; set; }
    public int SceneSeconds { get; set; }
    public bool ThinkScenes { get; set; }
    public IReadOnlyList<VideoProjectSceneDto> Scenes { get; set; } = Array.Empty<VideoProjectSceneDto>();
}

public sealed class VideoProjectSplitResult
{
    public long ProjectId { get; set; }
    public int SceneCount { get; set; }
    public List<VideoProjectSceneDto> Scenes { get; set; } = new();
}
