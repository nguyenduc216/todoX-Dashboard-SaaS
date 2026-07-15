using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services.AiCharacters;

public interface IAiCharacterService
{
    Task<IReadOnlyList<CharacterListItemDto>> GetCharactersAsync(CurrentUserSession user, string? keyword, string? status, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCharacterDto>> GetActiveCharactersAsync(CurrentUserSession user, CancellationToken ct = default);
    Task<CharacterDetailDto?> GetCharacterAsync(CurrentUserSession user, long id, CancellationToken ct = default);
    Task<CharacterDetailDto> CreateCharacterAsync(CreateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<CharacterDetailDto> UpdateCharacterAsync(long id, UpdateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task DisableCharacterAsync(long id, CurrentUserSession user, CancellationToken ct = default);
    Task<GenerateCharacterImageResponse> GenerateImageAsync(GenerateCharacterImageRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<GenerateCharacterImageResponse> UploadMasterImageAsync(long characterId, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task SetMasterImageAsync(long characterId, long renderId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class AiCharacterService : IAiCharacterService
{
    private static readonly HashSet<string> AspectRatios = new(StringComparer.OrdinalIgnoreCase) { "1:1", "9:16", "16:9", "4:5", "3:4" };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase) { "active", "inactive" };

    private readonly AiCharacterRepository _repo;
    private readonly AiProviderRepository _providerRepo;
    private readonly IAiImageProviderFactory _providers;
    private readonly IAiProviderService _aiProviders;
    private readonly IMediaFileService _media;
    private readonly CharacterPromptBuilder _promptBuilder;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<AiCharacterService> _logger;

    public AiCharacterService(AiCharacterRepository repo, AiProviderRepository providerRepo, IAiImageProviderFactory providers,
        IAiProviderService aiProviders, IMediaFileService media, CharacterPromptBuilder promptBuilder,
        TenantContext tenant, IConfiguration config, ILogger<AiCharacterService> logger)
    {
        _repo = repo;
        _providerRepo = providerRepo;
        _providers = providers;
        _aiProviders = aiProviders;
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

    public Task<CharacterDetailDto?> GetCharacterAsync(CurrentUserSession user, long id, CancellationToken ct = default)
        => _repo.GetAsync(Scope(user), id, ct);

    public async Task<CharacterDetailDto> CreateCharacterAsync(CreateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        ValidateCreate(request);
        await _tenant.EnsureLoadedAsync(ct);
        var scope = Scope(user);
        var providerCode = DefaultProviderCode();
        var modelName = DefaultModel();
        var characterCode = BuildCharacterCode(request.CharacterName);
        var style = CharacterPresetOptions.NormalizeOptionalPreset(request.StylePreset);
        var gender = CharacterPresetOptions.NormalizeOptionalPreset(request.Gender);
        var normalized = BuildRenderPrompt(request.RenderPrompt, request.CharacterName, request.Description, style, gender, request.AspectRatio);
        var negative = _promptBuilder.BuildNegativePrompt();
        _logger.LogInformation("AI_CHARACTER_CREATE_START userId={UserId} customerId={CustomerId} provider={ProviderCode} model={ModelName}",
            user.UserId, user.CustomerId, providerCode, modelName);

        const string charTable = "todox_ai_character";
        DbDiagnostics.LogFieldLengths(_logger, "character_insert",
            ("character_code", characterCode),
            ("provider_code", providerCode),
            ("model_name", modelName),
            ("status", "active"));

        long id;
        try
        {
            id = await _repo.InsertCharacterAsync(scope, new AiCharacter
            {
                CustomerId = scope.CustomerId,
                CharacterCode = DbDiagnostics.Clip(_logger, charTable, "character_code", characterCode)!,
                CharacterName = request.CharacterName.Trim(),
                Description = request.Description.Trim(),
                StylePreset = style,
                Gender = gender,
                AspectRatio = request.AspectRatio,
                MasterPrompt = string.IsNullOrWhiteSpace(request.RenderPrompt) ? request.Description.Trim() : request.RenderPrompt.Trim(),
                NormalizedPrompt = normalized,
                NegativePrompt = negative,
                ProviderCode = DbDiagnostics.Clip(_logger, charTable, "provider_code", providerCode)!,
                ModelName = DbDiagnostics.Clip(_logger, charTable, "model_name", modelName),
                Seed = request.Seed,
                Status = "active",
                CreatedBy = user.UserId.ToString(),
                UpdatedBy = user.UserId.ToString()
            }, ct);
        }
        catch (Exception ex)
        {
            DbDiagnostics.LogPostgresException(_logger, ex, "character_insert");
            throw;
        }

        // Lưu Character phải độc lập với render ảnh. Việc render (nếu người dùng chọn) được
        // thực hiện ở trang chi tiết sau khi Character đã được lưu, nên ở đây không gọi
        // GenerateImageAsync/provider AI/media storage.
        return await _repo.GetAsync(scope, id, ct)
               ?? throw new InvalidOperationException("Đã tạo Character nhưng không đọc lại được dữ liệu.");
    }

    public async Task<CharacterDetailDto> UpdateCharacterAsync(long id, UpdateCharacterRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        ValidateUpdate(request);
        var scope = Scope(user);
        var current = await _repo.GetAsync(scope, id, ct)
            ?? throw new InvalidOperationException("Không cập nhật được Character do sai ID hoặc customer scope.");
        var normalizedStatus = NormalizeCharacterStatus(request.Status, current.Status);
        var style = CharacterPresetOptions.NormalizeOptionalPreset(request.StylePreset);
        var gender = CharacterPresetOptions.NormalizeOptionalPreset(request.Gender);
        var normalized = BuildRenderPrompt(request.RenderPrompt, request.CharacterName, request.Description, style, gender, request.AspectRatio);
        var negative = _promptBuilder.BuildNegativePrompt();
        var masterImageExists = !string.IsNullOrWhiteSpace(current.MasterImageUrl);

        var updated = await _repo.UpdateCharacterAsync(scope, id, request, normalized, negative, normalizedStatus, current.Status, masterImageExists, user.UserId.ToString(), ct);

        if (!string.Equals(updated.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Lưu trạng thái Character không thành công. Yêu cầu={normalizedStatus}, thực tế={updated.Status}");
        }

        _logger.LogInformation("AI_CHARACTER_UPDATE_DONE characterId={CharacterId} statusBefore={StatusBefore} statusAfter={StatusAfter} masterImageExists={MasterImageExists}",
            id, current.Status, updated.Status, !string.IsNullOrWhiteSpace(updated.MasterImageUrl));
        return updated;
    }

    public Task DisableCharacterAsync(long id, CurrentUserSession user, CancellationToken ct = default)
        => _repo.DisableAsync(Scope(user), id, user.UserId.ToString(), ct);

    public async Task<GenerateCharacterImageResponse> GenerateImageAsync(GenerateCharacterImageRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var scope = Scope(user);
        CharacterDetailDto? character = null;
        if (request.CharacterId is long id)
        {
            character = await _repo.GetAsync(scope, id, ct)
                ?? throw new InvalidOperationException("Không tìm thấy Character hoặc bạn không có quyền truy cập.");
        }

        var characterName = request.CharacterName ?? character?.CharacterName ?? string.Empty;
        var description = request.Description ?? character?.Description ?? string.Empty;
        var style = CharacterPresetOptions.NormalizeOptionalPreset(request.StylePreset ?? character?.StylePreset);
        var gender = CharacterPresetOptions.NormalizeOptionalPreset(request.Gender ?? character?.Gender);
        var aspect = request.AspectRatio ?? character?.AspectRatio ?? _config["OpenRouter:Image:DefaultAspectRatio"] ?? "1:1";
        var seed = request.Seed ?? character?.Seed;
        ValidateGenerate(characterName, description, style, gender, aspect, request.ReferenceImageUrls);

        var providerCode = character?.ProviderCode ?? DefaultProviderCode();
        var modelName = character?.ModelName ?? DefaultModel();
        AiProviderDetailDto? providerDetail = null;
        AiProviderCapabilityDto? providerCapability = null;

        // Resolve the AI provider/model/cost from the database (admin-configured). Falls back to the
        // legacy appsettings default when no provider is configured for character_generation.
        ProviderOptionDto? providerOption = null;
        try
        {
            providerOption = await _aiProviders.ResolveProviderForCapabilityAsync(
                "character_generation", request.ProviderCapabilityId, fromUser: true, ct);
            providerCode = providerOption.ProviderCode;
            if (!string.IsNullOrWhiteSpace(providerOption.ModelName)) modelName = providerOption.ModelName!;
            providerDetail = await _providerRepo.GetProviderAsync(providerOption.ProviderId, ct);
            providerCapability = providerDetail?.Capabilities.FirstOrDefault(c => c.Id == providerOption.ProviderCapabilityId);
        }
        catch (Exception ex)
        {
            if (request.ProviderCapabilityId is not null) throw; // explicit selection must be valid
            _logger.LogInformation("AI_CHARACTER_PROVIDER_FALLBACK reason={Reason}", ex.Message);
        }

        var outputFormat = _config["OpenRouter:Image:DefaultOutputFormat"] ?? "png";
        var quality = _config["OpenRouter:Image:DefaultQuality"] ?? "high";
        var resolution = _config["OpenRouter:Image:DefaultResolution"] ?? "1K";
        var prompt = BuildRenderPrompt(request.RenderPrompt, characterName, description, style, gender, aspect);
        var renderCode = BuildRenderCode("CHR");
        long? renderId = null;

        _logger.LogInformation("AI_CHARACTER_IMAGE_GENERATE_START userId={UserId} characterId={CharacterId} provider={ProviderCode} model={ModelName} aspect={AspectRatio}",
            user.UserId, character?.Id, providerCode, modelName, aspect);

        var provider = _providers.GetProvider(ProviderCodeMap.ToFactoryKey(providerCode));
        var response = await provider.GenerateImageAsync(new OpenRouterImageRequest
        {
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            Model = modelName,
            Prompt = prompt,
            AspectRatio = aspect,
            OutputFormat = outputFormat,
            Quality = quality,
            Resolution = resolution,
            Seed = seed,
            Count = 1,
            FileCategory = BuildStorageCategory(scope, character?.CharacterCode ?? BuildCharacterCode(characterName)),
            ReferenceImageUrls = request.ReferenceImageUrls ?? Array.Empty<string>(),
            BaseUrlOverride = providerDetail?.BaseUrl,
            EndpointPath = providerCapability?.EndpointPath,
            ApiKeyConfigName = providerDetail?.ApiKeyConfigName,
            ProviderConfigJson = providerDetail?.ConfigJson,
            CapabilityConfigJson = providerCapability?.ConfigJson
        }, ct);

        string? imageUrl = null;
        string? objectKey = null;
        var status = response.Success ? "success" : "failed";
        var error = response.Success ? null : FriendlyError(response.ErrorMessage);
        if (response.Success && response.ImageBytes is { Length: > 0 })
        {
            var category = BuildStorageCategory(scope, character?.CharacterCode ?? BuildCharacterCode(characterName));
            var media = await _media.SaveAsync(response.ImageBytes, $"{renderCode}.{outputFormat}", response.MimeType ?? $"image/{outputFormat}",
                category, user.UserId, user.CustomerId, _tenant.TenantId, ct);
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
        if (characterId is long cid)
        {
            const string renderTable = "todox_ai_character_render";
            var renderProviderCode = response.ProviderCode ?? providerCode;
            var renderModelName = response.ModelName ?? modelName;

            DbDiagnostics.LogFieldLengths(_logger, "character_render_insert",
                ("render_code", renderCode),
                ("provider_code", renderProviderCode),
                ("model_name", renderModelName),
                ("output_format", outputFormat),
                ("quality", quality),
                ("resolution", resolution),
                ("status", status));
            DbDiagnostics.LogFieldLengths(_logger, "character_render_insert_detail",
                ("prompt", prompt),
                ("aspect_ratio", aspect),
                ("created_by", user.UserId.ToString()),
                ("error_message", error));

            var clippedPrompt = DbDiagnostics.Clip(_logger, renderTable, "prompt", prompt);
            var clippedError = DbDiagnostics.Clip(_logger, renderTable, "error_message", error);
            var clippedCreatedBy = DbDiagnostics.Clip(_logger, renderTable, "created_by", user.UserId.ToString());

            try
            {
                renderId = await _repo.InsertRenderAsync(scope, new AiCharacterRender
                {
                    CharacterId = cid,
                    CustomerId = scope.CustomerId,
                    RenderCode = DbDiagnostics.Clip(_logger, renderTable, "render_code", renderCode)!,
                    ProviderCode = DbDiagnostics.Clip(_logger, renderTable, "provider_code", renderProviderCode)!,
                    ModelName = DbDiagnostics.Clip(_logger, renderTable, "model_name", renderModelName),
                    Prompt = clippedPrompt!,
                    RequestJson = response.RawRequestJson,
                    ResponseJson = response.RawResponseJson,
                    OutputImageUrl = imageUrl,
                    OutputObjectKey = objectKey,
                    AspectRatio = aspect,
                    OutputFormat = DbDiagnostics.Clip(_logger, renderTable, "output_format", outputFormat)!,
                    Quality = DbDiagnostics.Clip(_logger, renderTable, "quality", quality),
                    Resolution = DbDiagnostics.Clip(_logger, renderTable, "resolution", resolution),
                    Seed = seed,
                    UsageCost = response.UsageCost,
                    UsageJson = response.UsageJson,
                    Status = DbDiagnostics.Clip(_logger, renderTable, "status", status)!,
                    ErrorMessage = clippedError,
                    CreatedBy = clippedCreatedBy
                }, ct);
            }
            catch (Exception ex)
            {
                DbDiagnostics.LogPostgresException(_logger, ex, "character_render_insert");
                throw;
            }

            if (response.Success && request.SaveAsMaster)
            {
                await _repo.SetMasterAsync(scope, cid, renderId.Value, user.UserId.ToString(), ct);
            }

            if (request.ReferenceImageUrls?.Length > 0)
            {
                await _repo.AddReferencesAsync(scope, cid, request.ReferenceImageUrls, user.UserId.ToString(), ct);
            }
        }

        // Record usage against the resolved provider capability (points come from the DB, not code).
        if (providerOption is not null)
        {
            await _aiProviders.LogUsageAsync(new AiProviderUsageLog
            {
                CustomerId = scope.CustomerId,
                ProviderId = providerOption.ProviderId,
                ProviderCapabilityId = providerOption.ProviderCapabilityId,
                ProviderCode = providerOption.ProviderCode,
                CapabilityCode = providerOption.CapabilityCode,
                FeatureCode = "character_manager",
                ModelName = response.ModelName ?? providerOption.ModelName ?? modelName,
                RequestId = renderCode,
                Quantity = 1,
                UnitType = providerOption.UnitType,
                UnitCostPoints = providerOption.UnitCostPoints,
                TotalPoints = providerOption.UnitCostPoints,
                ProviderRawCost = response.UsageCost,
                Status = status == "success" ? "success" : "failed",
                ErrorMessage = error,
                CreatedBy = user.UserId.ToString()
            }, ct);
        }

        return new GenerateCharacterImageResponse
        {
            CharacterId = characterId,
            RenderId = renderId,
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

    public Task SetMasterImageAsync(long characterId, long renderId, CurrentUserSession user, CancellationToken ct = default)
        => _repo.SetMasterAsync(Scope(user), characterId, renderId, user.UserId.ToString(), ct);

    public async Task<GenerateCharacterImageResponse> UploadMasterImageAsync(long characterId, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var scope = Scope(user);
        var character = await _repo.GetAsync(scope, characterId, ct)
            ?? throw new InvalidOperationException("Không tìm thấy Character hoặc bạn không có quyền truy cập.");

        if (content.Length == 0) throw new InvalidOperationException("File ảnh đang rỗng.");
        if (content.Length > 12 * 1024 * 1024) throw new InvalidOperationException("File ảnh tối đa 12MB.");
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chỉ hỗ trợ upload file hình ảnh.");
        }

        var category = BuildStorageCategory(scope, character.CharacterCode);
        var media = await _media.SaveAsync(content, fileName, contentType, category, user.UserId, user.CustomerId, _tenant.TenantId, ct);
        var imageUrl = media.PublicUrl ?? media.FileUrl;
        var objectKey = media.ObjectKey;
        await _repo.UpdateMasterImageAsync(scope, characterId, imageUrl, objectKey, user.UserId.ToString(), ct);

        var renderCode = BuildRenderCode("CHRUP");
        var renderId = await _repo.InsertRenderAsync(scope, new AiCharacterRender
        {
            CharacterId = characterId,
            CustomerId = scope.CustomerId,
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
            Status = "success",
            CreatedBy = user.UserId.ToString()
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
            Status = "success"
        };
    }

    private CharacterScope Scope(CurrentUserSession user)
    {
        return new CharacterScope(ToBigIntCustomerId(user.CustomerId ?? user.UserId));
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
        => $"customers/{scope.CustomerId}/characters/{characterCode}";

    private static string BuildCharacterCode(string name)
    {
        var code = new string(name.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (code.Contains("--", StringComparison.Ordinal)) code = code.Replace("--", "-", StringComparison.Ordinal);
        code = code.Trim('-');
        if (string.IsNullOrWhiteSpace(code)) code = "ai-character";
        var suffix = $"{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        return $"CHAR{suffix}"[..Math.Min(50, 4 + suffix.Length)];
    }

    private static string BuildRenderCode(string prefix)
    {
        var suffix = $"{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        return $"{prefix}{suffix}"[..Math.Min(50, prefix.Length + suffix.Length)];
    }

    private static long ToBigIntCustomerId(Guid id)
    {
        var bytes = id.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0);
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }

    private string BuildRenderPrompt(string? promptOverride, string characterName, string description, string? style, string? gender, string aspect)
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
    }

    private static string NormalizeCharacterStatus(string? status, string fallback)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is "active" or "inactive" ? normalized : fallback.Trim().ToLowerInvariant();
    }

    private static void ValidateGenerate(string? name, string? description, string? style, string? gender, string? aspect, string[]? references)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255) throw new InvalidOperationException("Character name bat buoc va toi da 255 ky tu.");
        if (string.IsNullOrWhiteSpace(description) || description.Length > 5000) throw new InvalidOperationException("Description bat buoc va toi da 5000 ky tu.");
        if (string.IsNullOrWhiteSpace(aspect) || !AspectRatios.Contains(aspect)) throw new InvalidOperationException("Aspect ratio khong hop le.");
        if (!CharacterPresetOptions.IsAllowedOptional(style, CharacterPresetOptions.StylePresets)) throw new InvalidOperationException("Style preset khong hop le.");
        if (!CharacterPresetOptions.IsAllowedOptional(gender, CharacterPresetOptions.GenderOptions)) throw new InvalidOperationException("Gender khong hop le.");
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




