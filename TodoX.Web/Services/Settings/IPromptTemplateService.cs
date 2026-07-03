namespace TodoX.Web.Services.Settings;

public interface IPromptTemplateService
{
    Task<PromptTemplateDto?> GetDefaultAsync(string promptType, string languageCode = "vi", CancellationToken ct = default);
    Task<PromptTemplateDto?> GetByCodeAsync(string promptCode, CancellationToken ct = default);
    Task<IReadOnlyList<PromptTemplateDto>> ListAsync(string? promptType = null, CancellationToken ct = default);
    Task<IReadOnlyList<PromptTemplateVersionDto>> GetVersionsAsync(Guid promptTemplateId, CancellationToken ct = default);
    Task<PromptTemplateDto> SaveAsync(PromptTemplateEditModel model, Guid? userId, CancellationToken ct = default);
}
