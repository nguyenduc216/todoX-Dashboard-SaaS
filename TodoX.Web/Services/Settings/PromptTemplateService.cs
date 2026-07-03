namespace TodoX.Web.Services.Settings;

public sealed class PromptTemplateService : IPromptTemplateService
{
    private readonly PromptTemplateRepository _repository;

    public PromptTemplateService(PromptTemplateRepository repository)
    {
        _repository = repository;
    }

    public Task<PromptTemplateDto?> GetDefaultAsync(string promptType, string languageCode = "vi", CancellationToken ct = default)
        => _repository.GetDefaultAsync(promptType, languageCode, ct);

    public Task<PromptTemplateDto?> GetByCodeAsync(string promptCode, CancellationToken ct = default)
        => _repository.GetByCodeAsync(promptCode, ct);

    public Task<IReadOnlyList<PromptTemplateDto>> ListAsync(string? promptType = null, CancellationToken ct = default)
        => _repository.ListAsync(promptType, ct);

    public Task<IReadOnlyList<PromptTemplateVersionDto>> GetVersionsAsync(Guid promptTemplateId, CancellationToken ct = default)
        => _repository.GetVersionsAsync(promptTemplateId, ct);

    public Task<PromptTemplateDto> SaveAsync(PromptTemplateEditModel model, Guid? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.PromptCode)) throw new InvalidOperationException("Ma prompt khong duoc de trong.");
        if (string.IsNullOrWhiteSpace(model.PromptName)) throw new InvalidOperationException("Ten prompt khong duoc de trong.");
        if (string.IsNullOrWhiteSpace(model.TemplateText)) throw new InvalidOperationException("Noi dung prompt khong duoc de trong.");
        if (string.IsNullOrWhiteSpace(model.PromptType)) model.PromptType = "avatar_chibi";
        if (string.IsNullOrWhiteSpace(model.LanguageCode)) model.LanguageCode = "vi";
        if (string.IsNullOrWhiteSpace(model.PromptGroup)) model.PromptGroup = "image";
        return _repository.SaveAsync(model, userId, ct);
    }
}
