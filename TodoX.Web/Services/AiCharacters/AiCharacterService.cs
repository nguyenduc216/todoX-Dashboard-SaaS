using TodoX.Web.Models;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services.AiCharacters;

public interface IAiCharacterService
{
    Task<IReadOnlyList<CharacterListItemDto>> GetCharactersAsync(CurrentUserSession user, string? keyword, string? status, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCharacterDto>> GetActiveCharactersAsync(CurrentUserSession user, CancellationToken ct = default);
    Task<CharacterDetailDto?> GetCharacterAsync(CurrentUserSession user, Guid id, CancellationToken ct = default);
    Task<CharacterDetailDto> CreateCharacterAsync(CreateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task UpdateCharacterAsync(Guid id, UpdateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task DisableCharacterAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<GenerateCharacterImageResponse> GenerateImageAsync(GenerateCharacterImageRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<GenerateCharacterImageResponse> UploadMasterImageAsync(Guid characterId, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task SetMasterImageAsync(Guid characterId, Guid renderId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class AiCharacterService : IAiCharacterService
{
    private static readonly HashSet<string> AspectRatios = new(StringComparer.OrdinalIgnoreCase) { "1:1", "9:16", "16:9", "4:5", "3:4" };
    private static readonly HashSet<string> StylePresets = new(StringComparer.OrdinalIgnoreCase) { "3d_chibi", "realistic", "anime", "cartoon", "corporate_mascot", "koc_ai" };
    private static readonly HashSet<string> Genders = new(StringComparer.OrdinalIgnoreCase) { "male", "female", "neutral", "not_specified" };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase) { "active", "inactive" };

    private readonly AiCharacterRepository _repo;
    private readonly IAiImageProviderFactory _providers;
    private readonly IMediaFileService _media;
    private readonly CharacterPromptBuilder _promptBuilder;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<AiCharacterService> _logger;

    public AiCharacterService(AiCharacterRepository repo, IAiImageProviderFactory providers,
        IMediaFileService media, CharacterPromptBuilder promptBuilder, TenantContext tenant,
        IConfiguration config, ILogger<AiCharacterService> logger)
    {
        _repo = repo;
        _providers = providers;
        _media = media;
        _promptBuilder = promptBuilder;
        _tenant = tenant;
        _config = config;
        _logger = logger;
    }

    public Task<IReadOnlyList<CharacterListItemDto>> GetCharactersAsync(CurrentUserSession user, string? keyword, string? status, CancellationToken ct = default)
        => _repo.ListAsync(Scope(user), keyword, status, ct);

    public Task<IReadOnlyList<ActiveCharacterDto>> GetActiveCharactersAsync(CurrentUserSession user, CancellationToken ct = default)
        => _repo.ListActiveAsync(Scope(user), ct);

    public Task<CharacterDetailDto?> GetCharacterAsync(CurrentUserSession user, Guid id, CancellationToken ct = default)
        => _repo.GetAsync(Scope(user), id, ct);

    public async Task<CharacterDetailDto> CreateCharacterAsync(CreateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        ValidateCreate(request);
        await _tenant.EnsureLoadedAsync(ct);
        var scope = Scope(user);
        var providerCode = DefaultProviderCode();
        var modelName = DefaultModel();
        var characterCode = BuildCharacterCode(request.CharacterName);
        var normalized = BuildRenderPrompt(request.RenderPrompt, request.CharacterName, request.Description, request.StylePreset, request.Gender, request.AspectRatio);
        var negative = _promptBuilder.BuildNegativePrompt();
        var id = Guid.NewGuid();

        _logger.LogInformation("AI_CHARACTER_CREATE_START userId={UserId} customerId={CustomerId} provider={ProviderCode} model={ModelName}",
            user.UserId, user.CustomerId, providerCode, modelName);

        await _repo.InsertCharacterAsync(scope, new AiCharacter
        {
            Id = id,
            CustomerId = scope.CustomerId,
            TenantId = scope.TenantId,
            CharacterCode = characterCode,
            CharacterName = request.CharacterName.Trim(),
            Description = request.Description.Trim(),
            StylePreset = request.StylePreset,
            Gender = request.Gender,
            AspectRatio = request.AspectRatio,
            MasterPrompt = string.IsNullOrWhiteSpace(request.RenderPrompt) ? request.Description.Trim() : request.RenderPrompt.Trim(),
            NormalizedPrompt = normalized,
            NegativePrompt = negative,
            ProviderCode = providerCode,
            ModelName = modelName,
            Seed = request.Seed,
            Status = "active",
            CreatedBy = user.UserId,
            UpdatedBy = user.UserId
        }, ct);

        if (request.AutoGenerateImage)
        {
            await GenerateImageAsync(new GenerateCharacterImageRequest
            {
                CharacterId = id,
                SaveAsMaster = true,
                Seed = request.Seed,
                RenderPrompt = request.RenderPrompt
            }, user, ct);
        }

        return await _repo.GetAsync(scope, id, ct)
               ?? throw new InvalidOperationException("Khong doc lai duoc AI character vua tao.");
    }

    public async Task UpdateCharacterAsync(Guid id, UpdateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        ValidateUpdate(request);
        var normalized = BuildRenderPrompt(request.RenderPrompt, request.CharacterName, request.Description, request.StylePreset, request.Gender, request.AspectRatio);
        var negative = _promptBuilder.BuildNegativePrompt();
        await _repo.UpdateCharacterAsync(Scope(user), id, request, normalized, negative, user.UserId, ct);
    }

    public Task DisableCharacterAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
        => _repo.DisableAsync(Scope(user), id, user.UserId, ct);

    public async Task<GenerateCharacterImageResponse> GenerateImageAsync(GenerateCharacterImageRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var scope = Scope(user);
        CharacterDetailDto? character = null;
        if (request.CharacterId is Guid id)
        {
            character = await _repo.GetAsync(scope, id, ct)
                ?? throw new InvalidOperationException("Khong tim thay AI character hoac ban khong co quyen truy cap.");
        }

        var characterName = request.CharacterName ?? character?.CharacterName ?? string.Empty;
        var description = request.Description ?? character?.Description ?? string.Empty;
        var style = request.StylePreset ?? character?.StylePreset ?? "3d_chibi";
        var gender = request.Gender ?? character?.Gender ?? "not_specified";
        var aspect = request.AspectRatio ?? character?.AspectRatio ?? _config["OpenRouter:Image:DefaultAspectRatio"] ?? "1:1";
        var seed = request.Seed ?? character?.Seed;
        ValidateGenerate(characterName, description, style, gender, aspect, request.ReferenceImageUrls);

        var providerCode = character?.ProviderCode ?? DefaultProviderCode();
        var modelName = character?.ModelName ?? DefaultModel();
        var outputFormat = _config["OpenRouter:Image:DefaultOutputFormat"] ?? "png";
        var quality = _config["OpenRouter:Image:DefaultQuality"] ?? "high";
        var resolution = _config["OpenRouter:Image:DefaultResolution"] ?? "1K";
        var prompt = BuildRenderPrompt(request.RenderPrompt, characterName, description, style, gender, aspect);
        var renderCode = $"CHR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28];
        var renderId = Guid.NewGuid();

        _logger.LogInformation("AI_CHARACTER_IMAGE_GENERATE_START userId={UserId} characterId={CharacterId} provider={ProviderCode} model={ModelName} aspect={AspectRatio}",
            user.UserId, character?.Id, providerCode, modelName, aspect);

        var provider = _providers.GetProvider(providerCode);
        var response = await provider.GenerateImageAsync(new OpenRouterImageRequest
        {
            UserId = user.UserId,
            CustomerId = scope.CustomerId,
            Model = modelName,
            Prompt = prompt,
            AspectRatio = aspect,
            OutputFormat = outputFormat,
            Quality = quality,
            Resolution = resolution,
            Seed = seed,
            Count = 1,
            FileCategory = BuildStorageCategory(scope, character?.CharacterCode ?? BuildCharacterCode(characterName)),
            ReferenceImageUrls = request.ReferenceImageUrls ?? Array.Empty<string>()
        }, ct);

        string? imageUrl = null;
        string? objectKey = null;
        var status = response.Success ? "completed" : "failed";
        var error = response.Success ? null : FriendlyError(response.ErrorMessage);
        if (response.Success && response.ImageBytes is { Length: > 0 })
        {
            var category = BuildStorageCategory(scope, character?.CharacterCode ?? BuildCharacterCode(characterName));
            var media = await _media.SaveAsync(response.ImageBytes, $"{renderCode}.{outputFormat}", response.MimeType ?? $"image/{outputFormat}",
                category, user.UserId, scope.CustomerId, _tenant.TenantId, ct);
            imageUrl = media.PublicUrl ?? media.FileUrl;
            objectKey = media.ObjectKey;
            _logger.LogInformation("AI_CHARACTER_IMAGE_STORED renderId={RenderId} objectKey={ObjectKey} cost={Cost}", renderId, objectKey, response.UsageCost);
        }
        else if (response.Success && !string.IsNullOrWhiteSpace(response.ImageUrl))
        {
            imageUrl = response.ImageUrl;
            objectKey = response.ObjectKey;
            _logger.LogInformation("AI_CHARACTER_IMAGE_STORED_BY_PROVIDER renderId={RenderId} provider={ProviderCode} objectKey={ObjectKey} cost={Cost}",
                renderId, providerCode, objectKey, response.UsageCost);
        }
        else
        {
            _logger.LogWarning("AI_CHARACTER_IMAGE_FAILED characterId={CharacterId} status={StatusCode} error={Error}",
                character?.Id, response.StatusCode, error);
        }

        var characterId = character?.Id ?? request.CharacterId;
        if (characterId is Guid cid)
        {
            await _repo.InsertRenderAsync(scope, new AiCharacterRender
            {
                Id = renderId,
                CharacterId = cid,
                CustomerId = scope.CustomerId,
                TenantId = scope.TenantId,
                RenderCode = renderCode,
                ProviderCode = response.ProviderCode ?? providerCode,
                ModelName = response.ModelName ?? modelName,
                Prompt = prompt,
                RequestJson = response.RawRequestJson,
                ResponseJson = response.RawResponseJson,
                OutputImageUrl = imageUrl,
                OutputObjectKey = objectKey,
                AspectRatio = aspect,
                OutputFormat = outputFormat,
                Quality = quality,
                Resolution = resolution,
                Seed = seed,
                UsageCost = response.UsageCost,
                UsageJson = response.UsageJson,
                Status = status,
                ErrorMessage = error,
                CreatedBy = user.UserId
            }, ct);

            if (response.Success && request.SaveAsMaster)
            {
                await _repo.SetMasterAsync(scope, cid, renderId, user.UserId, ct);
            }

            if (request.ReferenceImageUrls?.Length > 0)
            {
                await _repo.AddReferencesAsync(scope, cid, request.ReferenceImageUrls, user.UserId, ct);
            }
        }

        return new GenerateCharacterImageResponse
        {
            CharacterId = characterId,
            RenderId = characterId is null ? null : renderId,
            ImageUrl = imageUrl,
            ObjectKey = objectKey,
            Prompt = prompt,
            ProviderCode = providerCode,
            ModelName = response.ModelName ?? modelName,
            UsageCost = response.UsageCost,
            Status = status,
            ErrorMessage = error
        };
    }

    public Task SetMasterImageAsync(Guid characterId, Guid renderId, CurrentUserSession user, CancellationToken ct = default)
        => _repo.SetMasterAsync(Scope(user), characterId, renderId, user.UserId, ct);

    public async Task<GenerateCharacterImageResponse> UploadMasterImageAsync(Guid characterId, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var scope = Scope(user);
        var character = await _repo.GetAsync(scope, characterId, ct)
            ?? throw new InvalidOperationException("Khong tim thay AI character hoac ban khong co quyen truy cap.");

        if (content.Length == 0) throw new InvalidOperationException("File anh dang rong.");
        if (content.Length > 12 * 1024 * 1024) throw new InvalidOperationException("File anh toi da 12MB.");
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chi ho tro upload file hinh anh.");
        }

        var category = BuildStorageCategory(scope, character.CharacterCode);
        var media = await _media.SaveAsync(content, fileName, contentType, category, user.UserId, scope.CustomerId, _tenant.TenantId, ct);
        var imageUrl = media.PublicUrl ?? media.FileUrl;
        var objectKey = media.ObjectKey;
        await _repo.UpdateMasterImageAsync(scope, characterId, imageUrl, objectKey, user.UserId, ct);

        var renderId = Guid.NewGuid();
        var renderCode = $"CHR-UP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28];
        await _repo.InsertRenderAsync(scope, new AiCharacterRender
        {
            Id = renderId,
            CharacterId = characterId,
            CustomerId = scope.CustomerId,
            TenantId = scope.TenantId,
            RenderCode = renderCode,
            ProviderCode = "manual_upload",
            ModelName = "upload_file",
            Prompt = "Upload anh master thu cong",
            OutputImageUrl = imageUrl,
            OutputObjectKey = objectKey,
            AspectRatio = character.AspectRatio,
            OutputFormat = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant(),
            Quality = "original",
            Resolution = "original",
            Seed = character.Seed,
            Status = "completed",
            CreatedBy = user.UserId
        }, ct);

        _logger.LogInformation("AI_CHARACTER_MASTER_UPLOADED userId={UserId} characterId={CharacterId} objectKey={ObjectKey}",
            user.UserId, characterId, objectKey);

        return new GenerateCharacterImageResponse
        {
            CharacterId = characterId,
            RenderId = renderId,
            ImageUrl = imageUrl,
            ObjectKey = objectKey,
            Prompt = "Upload anh master thu cong",
            ProviderCode = "manual_upload",
            ModelName = "upload_file",
            Status = "completed"
        };
    }

    private CharacterScope Scope(CurrentUserSession user)
    {
        var tenantId = _tenant.TenantId;
        return user.CustomerId is Guid customerId
            ? new CharacterScope(customerId, tenantId)
            : new CharacterScope(null, tenantId);
    }

    private string DefaultProviderCode()
        => _config["AiCharacters:DefaultImageProviderCode"]
           ?? _config["OpenRouter:DefaultImageProviderCode"]
           ?? "todox_image";

    private string DefaultModel()
        => _config["AiCharacters:DefaultImageModel"]
           ?? _config["OpenRouter:DefaultImageModel"]
           ?? "ImageAICreativeRender";

    private static string BuildStorageCategory(CharacterScope scope, string characterCode)
        => scope.CustomerId is Guid customerId
            ? $"customers/{customerId}/characters/{characterCode}"
            : $"tenants/{scope.TenantId}/characters/{characterCode}";

    private static string BuildCharacterCode(string name)
    {
        var code = new string(name.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (code.Contains("--", StringComparison.Ordinal)) code = code.Replace("--", "-", StringComparison.Ordinal);
        code = code.Trim('-');
        if (string.IsNullOrWhiteSpace(code)) code = "ai-character";
        return $"{code}-{Guid.NewGuid():N}"[..Math.Min(code.Length + 9, 48)];
    }

    private string BuildRenderPrompt(string? promptOverride, string characterName, string description, string style, string gender, string aspect)
        => string.IsNullOrWhiteSpace(promptOverride)
            ? _promptBuilder.BuildNormalizedPrompt(characterName, description, style, gender, aspect)
            : promptOverride.Trim();

    private static void ValidateCreate(CreateCharacterRequest request)
    {
        ValidateGenerate(request.CharacterName, request.Description, request.StylePreset, request.Gender, request.AspectRatio, null);
    }

    private static void ValidateUpdate(UpdateCharacterRequest request)
    {
        ValidateGenerate(request.CharacterName, request.Description, request.StylePreset, request.Gender, request.AspectRatio, null);
        if (!Statuses.Contains(request.Status)) throw new InvalidOperationException("Status khong hop le.");
    }

    private static void ValidateGenerate(string? name, string? description, string? style, string? gender, string? aspect, string[]? references)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) throw new InvalidOperationException("Character name bat buoc va toi da 255 ky tu.");
        if (string.IsNullOrWhiteSpace(description) || description.Length > 5000) throw new InvalidOperationException("Description bat buoc va toi da 5000 ky tu.");
        if (string.IsNullOrWhiteSpace(aspect) || !AspectRatios.Contains(aspect)) throw new InvalidOperationException("Aspect ratio khong hop le.");
        if (string.IsNullOrWhiteSpace(style) || !StylePresets.Contains(style)) throw new InvalidOperationException("Style preset khong hop le.");
        if (string.IsNullOrWhiteSpace(gender) || !Genders.Contains(gender)) throw new InvalidOperationException("Gender khong hop le.");
        if (references is null) return;
        foreach (var url in references.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Reference image URL phai la http/https hop le.");
            }
        }
    }

    private static string? FriendlyError(string? error)
        => string.IsNullOrWhiteSpace(error) ? "Provider render chua tao duoc anh. Vui long thu lai." : error;
}
