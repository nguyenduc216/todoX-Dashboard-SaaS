using System.Text.Json;
using Dapper;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Profile;

public sealed partial class ChibiAvatarService
{
    public async Task<ChibiGenerationDto> GenerateAsync(ChibiGenerateInput input, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var count = Math.Clamp(input.Count, 1, 6);
        var basePrompt = string.IsNullOrWhiteSpace(input.BasePromptOverride)
            ? BuildDefaultPrompt(input.Gender, input.AvatarMediaId is not null,
                input.LogoMediaId is not null, input.ProductMediaId is not null, input.SceneMediaId is not null)
            : input.BasePromptOverride!;

        var generationId = Guid.NewGuid();
        await InsertGenerationAsync(generationId, input, basePrompt);

        // Charge tokens up-front for the whole batch (customers only). Admin => no charge.
        var imageCost = await _tokenSettings.GetChibiImageCostAsync();
        var total = imageCost * count;
        var charge = await _wallet.ChargeAsync(
            input.IsCustomer ? input.CustomerId : null, input.UserId, total, count,
            "chibi_image", "google-vertex-ai", "imagen-3.0-generate-002", "Vertex-image-render",
            unit: "image", referenceId: generationId, referenceType: "chibi_generation");

        if (!charge.Ok)
        {
            await CompleteGenerationAsync(generationId, "failed", new List<ChibiImage>(), Guid.Empty, charge.Error);
            return new ChibiGenerationDto { Id = generationId, Status = "failed", Error = charge.Error };
        }

        // Ask Gemini for N distinct scenario prompts from the base prompt.
        List<string> prompts;
        try
        {
            prompts = await _gemini.GenerateVariationsAsync(basePrompt, count, ct);
            // Log the Gemini call as usage (no wallet charge; secondary call).
            var geminiCost = await _tokenSettings.GetGeminiPromptCostAsync();
            await _wallet.LogUsageOnlyAsync(input.IsCustomer ? input.CustomerId : null, input.UserId,
                "google-vertex-ai", _gemini.ModelCode, "gemini_prompt", 1, geminiCost, "gemini-generate",
                unit: "call", referenceId: generationId, referenceType: "chibi_generation");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini variation failed; using base prompt for all images.");
            prompts = Enumerable.Range(0, count).Select(_ => basePrompt).ToList();
        }

        // Render each prompt as one image; each becomes a render row.
        var images = new List<ChibiImage>();
        var anyFail = false;
        for (var i = 0; i < count; i++)
        {
            var prompt = prompts[Math.Min(i, prompts.Count - 1)];
            var result = await _render.RenderAsync(new ImageRenderRequestModel
            {
                Prompt = prompt,
                Count = 1,
                AspectRatio = "1:1",
                MimeType = "image/png",
                UserId = input.UserId,
                CustomerId = input.CustomerId,
                FileCategory = "chibi"
            }, ct);

            if (result.Ok && result.Data.Count > 0)
            {
                var d = result.Data[0];
                var renderId = await InsertRenderAsync(generationId, input.UserId, d.MediaId, d.Url, basePrompt, prompt, result.Model);
                images.Add(new ChibiImage { RenderId = renderId, MediaId = d.MediaId, Url = d.Url, PromptInput = basePrompt, PromptUsed = prompt });
            }
            else
            {
                anyFail = true;
                var renderId = await InsertRenderAsync(generationId, input.UserId, null, null, basePrompt, prompt, result.Model, "failed", result.Error);
                images.Add(new ChibiImage { RenderId = renderId, Status = "failed", PromptInput = basePrompt, PromptUsed = prompt, Error = result.Error });
            }
        }

        var okImages = images.Where(x => x.Status != "failed").ToList();
        var status = okImages.Count > 0 ? "completed" : "failed";
        await CompleteGenerationAsync(generationId, status, okImages, Guid.Empty,
            anyFail && okImages.Count == 0 ? images.FirstOrDefault(x => x.Error is not null)?.Error : null);

        // Surface the first concrete render error (full text) so the UI can show it for support.
        var firstError = images.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Error))?.Error;

        return new ChibiGenerationDto
        {
            Id = generationId,
            Status = status,
            GeneratedPrompt = basePrompt,
            Images = images,
            Charged = charge.Charged,
            BalanceAfter = charge.BalanceAfter,
            Error = firstError,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<ChibiImage> RerenderAsync(Guid userId, Guid? customerId, bool isCustomer, Guid renderId, string prompt, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);

        // Verify the render belongs to the user.
        Guid generationId;
        using (var conn = await _factory.OpenAsync(ct))
        {
            var gen = await conn.ExecuteScalarAsync<Guid?>(
                "SELECT generation_id FROM auth.user_avatar_renders WHERE id=@id AND user_id=@uid;",
                new { id = renderId, uid = userId });
            if (gen is null) throw new InvalidOperationException("Bản render không thuộc về người dùng.");
            generationId = gen.Value;
        }

        // Charge one image (customers only). Edited prompt => NO Gemini optimization.
        var imageCost = await _tokenSettings.GetChibiImageCostAsync();
        var charge = await _wallet.ChargeAsync(isCustomer ? customerId : null, userId, imageCost, 1,
            "chibi_image", "google-vertex-ai", "imagen-3.0-generate-002", "Vertex-image-render",
            unit: "image", referenceId: renderId, referenceType: "chibi_rerender");
        if (!charge.Ok)
        {
            throw new InvalidOperationException(charge.Error ?? "Không đủ token.");
        }

        var result = await _render.RenderAsync(new ImageRenderRequestModel
        {
            Prompt = prompt,
            Count = 1,
            AspectRatio = "1:1",
            MimeType = "image/png",
            UserId = userId,
            CustomerId = customerId,
            FileCategory = "chibi"
        }, ct);

        if (!result.Ok || result.Data.Count == 0)
        {
            await UpdateRenderAsync(renderId, null, null, prompt, prompt, "failed", result.Error);
            throw new InvalidOperationException(result.Error ?? "Vertex render lỗi.");
        }

        var d = result.Data[0];
        await UpdateRenderAsync(renderId, d.MediaId, d.Url, prompt, prompt, "completed", null);
        return new ChibiImage { RenderId = renderId, MediaId = d.MediaId, Url = d.Url, PromptInput = prompt, PromptUsed = prompt };
    }

    // ---------- DB helpers ----------

    private async Task InsertGenerationAsync(Guid id, ChibiGenerateInput input, string prompt)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_chibi_generations
                (id, tenant_id, user_id, status, prompt, generated_prompt, gender,
                 reference_avatar_media_id, reference_logo_media_id, reference_product_media_id, reference_scene_media_id, created_at)
            VALUES
                (@id, @tenant, @uid, 'processing', @prompt, @prompt, @gender,
                 @avatar, @logo, @product, @scene, now());
            """,
            new
            {
                id, tenant = _tenant.TenantId, uid = input.UserId, prompt, gender = input.Gender,
                avatar = input.AvatarMediaId, logo = input.LogoMediaId,
                product = input.ProductMediaId, scene = input.SceneMediaId
            });
    }

    private async Task<Guid> InsertRenderAsync(Guid generationId, Guid userId, Guid? mediaId, string? url,
        string promptInput, string promptUsed, string model, string status = "completed", string? error = null)
    {
        using var conn = await _factory.OpenAsync();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_avatar_renders
                (id, tenant_id, user_id, generation_id, media_id, image_url, prompt_input, prompt_used, model, status, error_message, created_at)
            VALUES
                (@id, @tenant, @uid, @gen, @media, @url, @pin, @pused, @model, @status, @err, now());
            """,
            new { id, tenant = _tenant.TenantId, uid = userId, gen = generationId, media = mediaId,
                  url, pin = promptInput, pused = promptUsed, model, status, err = error });
        return id;
    }

    private async Task UpdateRenderAsync(Guid renderId, Guid? mediaId, string? url, string promptInput, string promptUsed, string status, string? error)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE auth.user_avatar_renders
               SET media_id=@media, image_url=@url, prompt_input=@pin, prompt_used=@pused,
                   status=@status, error_message=@err, updated_at=now()
             WHERE id=@id;
            """,
            new { id = renderId, media = mediaId, url, pin = promptInput, pused = promptUsed, status, err = error });
    }

    private async Task CompleteGenerationAsync(Guid id, string status, List<ChibiImage> images, Guid vertexRequestId, string? error)
    {
        using var conn = await _factory.OpenAsync();
        var json = JsonSerializer.Serialize(images.Select(i => new { renderId = i.RenderId, mediaId = i.MediaId, url = i.Url, promptUsed = i.PromptUsed }));
        await conn.ExecuteAsync(
            """
            UPDATE auth.user_chibi_generations
               SET status=@status, result=@json::jsonb, error_message=@err, completed_at=now()
             WHERE id=@id;
            """, new { id, status, json, err = error });
    }

    public async Task<IReadOnlyList<ChibiGenerationDto>> GetGenerationsAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var gens = await conn.QueryAsync<(Guid Id, string Status, string? GeneratedPrompt, Guid? SelectedMediaId, DateTime CreatedAt)>(
            """
            SELECT id AS Id, status AS Status, generated_prompt AS GeneratedPrompt,
                   selected_media_id AS SelectedMediaId, created_at AS CreatedAt
              FROM auth.user_chibi_generations WHERE user_id=@uid ORDER BY created_at DESC LIMIT 20;
            """, new { uid = userId });

        var list = new List<ChibiGenerationDto>();
        foreach (var g in gens)
        {
            var renders = await conn.QueryAsync<ChibiImage>(
                """
                SELECT id AS RenderId, media_id AS MediaId, image_url AS Url,
                       prompt_input AS PromptInput, prompt_used AS PromptUsed, status AS Status
                  FROM auth.user_avatar_renders WHERE generation_id=@gid ORDER BY created_at;
                """, new { gid = g.Id });
            list.Add(new ChibiGenerationDto
            {
                Id = g.Id, Status = g.Status, GeneratedPrompt = g.GeneratedPrompt,
                SelectedMediaId = g.SelectedMediaId, CreatedAt = g.CreatedAt,
                Images = renders.ToList()
            });
        }
        return list;
    }

    public async Task SelectAsync(Guid userId, Guid generationId, Guid mediaId, CancellationToken ct = default)
    {
        using (var conn = await _factory.OpenAsync(ct))
        {
            var owns = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM media.media_files WHERE id=@m AND user_id=@u);",
                new { m = mediaId, u = userId });
            if (!owns) throw new InvalidOperationException("Ảnh không thuộc về người dùng.");

            await conn.ExecuteAsync(
                "UPDATE auth.user_chibi_generations SET status='selected', selected_media_id=@media, selected_at=now() WHERE id=@id AND user_id=@uid;",
                new { id = generationId, uid = userId, media = mediaId });
        }
        await _avatars.SetActiveFromMediaAsync(userId, mediaId, "chibi", ct);
    }
}
