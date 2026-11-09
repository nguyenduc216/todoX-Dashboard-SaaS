using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.CustomerVideos;

/// <summary>Point price of one kind of video TodoX produces for customers.</summary>
public sealed class CustomerVideoProduct
{
    public string ProductCode { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Points { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A finished video TodoX added to a customer account.</summary>
public sealed class CustomerVideo
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string ProductCode { get; set; } = CustomerVideoRules.Communication;
    public string? ProductName { get; set; }
    public string Title { get; set; } = "";
    public string? VideoUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string? Note { get; set; }
    public string? Script { get; set; }
    public decimal ChargedPoints { get; set; }
    public string ChargeStatus { get; set; } = CustomerVideoRules.StatusPending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed record CustomerVideoSaveResult(bool Ok, Guid? Id, string? Error);

/// <summary>Pure rules (unit-tested).</summary>
public static class CustomerVideoRules
{
    public const string Communication = "truyen_thong";
    public const string Tvc = "tvc";
    public const string StatusPending = "pending";
    public const string StatusCharged = "charged";
    public const string StatusFree = "free";
    public const string StatusRefunded = "refunded";

    /// <summary>billing.token_transactions.reference_type of a charge, so a retry never charges twice.</summary>
    public const string ReferenceType = "customer_video";

    public static readonly IReadOnlyList<string> ProductCodes = [Communication, Tvc];

    public static string DefaultName(string productCode) => productCode switch
    {
        Tvc => "Video TVC doanh nghiệp",
        _ => "Video truyền thông"
    };

    public static string StatusLabel(string? status) => status switch
    {
        StatusCharged => "Đã trừ điểm",
        StatusFree => "Miễn phí",
        StatusRefunded => "Đã hoàn điểm",
        _ => "Chưa trừ điểm"
    };

    public static string? Validate(CustomerVideo video, decimal points)
    {
        if (video.CustomerId == Guid.Empty) return "Vui lòng chọn khách hàng.";
        if (!ProductCodes.Contains(video.ProductCode)) return "Loại video không hợp lệ.";
        if (string.IsNullOrWhiteSpace(video.Title)) return "Vui lòng nhập tiêu đề video.";
        if (video.Title.Trim().Length > 300) return "Tiêu đề tối đa 300 ký tự.";
        if (string.IsNullOrWhiteSpace(video.VideoUrl)) return "Vui lòng tải video lên.";
        if (points < 0) return "Số điểm không hợp lệ.";
        return null;
    }
}

/// <summary>
/// studio.* (database/manual/customer-videos/20261110_customer_videos.sql): point prices per video kind and the
/// videos TodoX hands to customers. Points are charged through <see cref="WalletService"/> unchanged.
/// </summary>
public sealed class CustomerVideoRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly WalletService _wallets;
    private readonly AuthStateService _auth;
    private readonly ILogger<CustomerVideoRepository> _logger;

    public CustomerVideoRepository(TodoXConnectionFactory factory, WalletService wallets, AuthStateService auth, ILogger<CustomerVideoRepository> logger)
    {
        _factory = factory;
        _wallets = wallets;
        _auth = auth;
        _logger = logger;
    }

    private CurrentUserSession RequireAdmin()
    {
        var user = _auth.CurrentUser;
        if (!AdminEndpointAuthorization.IsAdmin(user)) throw new UnauthorizedAccessException("Chỉ quản trị viên TodoX được quản lý video khách hàng.");
        return user!;
    }

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            return await conn.ExecuteScalarAsync<bool>(
                "SELECT to_regclass('studio.customer_videos') IS NOT NULL AND to_regclass('studio.video_product_prices') IS NOT NULL;");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer video schema check failed.");
            return false;
        }
    }

    public async Task<IReadOnlyList<CustomerVideoProduct>> GetProductsAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CustomerVideoProduct>("""
            SELECT product_code AS ProductCode, name AS Name, points AS Points, description AS Description,
                   is_active AS IsActive, sort_order AS SortOrder, updated_at AS UpdatedAt
              FROM studio.video_product_prices
             ORDER BY sort_order, product_code;
            """);
        return rows.ToList();
    }

    public async Task SaveProductAsync(CustomerVideoProduct product)
    {
        var actor = RequireAdmin();
        if (!CustomerVideoRules.ProductCodes.Contains(product.ProductCode)) throw new ArgumentException("Loại video không hợp lệ.");
        if (product.Points < 0) throw new ArgumentException("Số điểm không hợp lệ.");
        var name = string.IsNullOrWhiteSpace(product.Name) ? CustomerVideoRules.DefaultName(product.ProductCode) : product.Name.Trim();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("""
            UPDATE studio.video_product_prices
               SET name=@name, points=@points, description=@description, is_active=@active,
                   updated_at=now(), updated_by=@actor
             WHERE product_code=@code;
            """,
            new { code = product.ProductCode, name, points = product.Points, description = Blank(product.Description), active = product.IsActive, actor = actor.UserId });
    }

    private const string SelectVideo = """
        SELECT v.id AS Id, v.customer_id AS CustomerId, coalesce(nullif(c.company_name, ''), c.full_name) AS CustomerName,
               v.product_code AS ProductCode, p.name AS ProductName, v.title AS Title,
               v.video_url AS VideoUrl, v.poster_url AS PosterUrl, v.note AS Note, v.script AS Script,
               v.charged_points AS ChargedPoints, v.charge_status AS ChargeStatus,
               v.created_at AS CreatedAt, v.updated_at AS UpdatedAt
          FROM studio.customer_videos v
          LEFT JOIN studio.video_product_prices p ON p.product_code = v.product_code
          LEFT JOIN crm.customers c ON c.id = v.customer_id
        """;

    public async Task<IReadOnlyList<CustomerVideo>> ListAllAsync(int take = 500)
    {
        RequireAdmin();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CustomerVideo>(SelectVideo + " WHERE v.charge_status <> 'pending' ORDER BY v.created_at DESC LIMIT @take;", new { take });
        return rows.ToList();
    }

    /// <summary>The signed-in customer's own videos, newest first.</summary>
    public async Task<IReadOnlyList<CustomerVideo>> ListOwnedAsync(CurrentUserSession user, int take)
    {
        if (user is not { IsCustomer: true, CustomerId: Guid customerId }) return [];
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CustomerVideo>(
            SelectVideo + " WHERE v.customer_id=@customerId AND v.charge_status <> 'pending' ORDER BY v.created_at DESC LIMIT @take;",
            new { customerId, take });
        return rows.ToList();
    }

    /// <summary>One video, only for its own customer or a TodoX admin.</summary>
    public async Task<CustomerVideo?> GetAsync(Guid id, CurrentUserSession? user)
    {
        if (user?.IsAuthenticated != true) return null;
        using var conn = await _factory.OpenAsync();
        var video = await conn.QuerySingleOrDefaultAsync<CustomerVideo>(SelectVideo + " WHERE v.id=@id AND v.charge_status <> 'pending';", new { id });
        if (video is null) return null;
        if (AdminEndpointAuthorization.IsAdmin(user)) return video;
        return user.IsCustomer && user.CustomerId == video.CustomerId ? video : null;
    }

    /// <summary>
    /// Adds a video to a customer and charges <paramref name="points"/> from their wallet (0 = free).
    /// Nothing is kept when the customer does not have enough points.
    /// </summary>
    public async Task<CustomerVideoSaveResult> CreateAsync(CustomerVideo video, decimal points)
    {
        var actor = RequireAdmin();
        var error = CustomerVideoRules.Validate(video, points);
        if (error is not null) return new(false, null, error);

        var id = Guid.NewGuid();
        using (var conn = await _factory.OpenAsync())
        {
            await conn.ExecuteAsync("""
                INSERT INTO studio.customer_videos
                    (id, customer_id, product_code, title, video_url, poster_url, note, script,
                     charged_points, charge_status, created_at, created_by, updated_at, updated_by)
                VALUES (@id, @customer, @product, @title, @video, @poster, @note, @script,
                        @points, 'pending', now(), @actor, now(), @actor);
                """,
                new
                {
                    id, customer = video.CustomerId, product = video.ProductCode, title = video.Title.Trim(),
                    video = video.VideoUrl, poster = Blank(video.PosterUrl), note = Blank(video.Note), script = Blank(video.Script),
                    points, actor = actor.UserId
                });
        }

        if (points > 0)
        {
            ChargeResult charge;
            try
            {
                charge = await _wallets.ChargeAsync(video.CustomerId, actor.UserId, points, 1,
                    CustomerVideoRules.DefaultName(video.ProductCode), "todox", video.ProductCode, "customer_video", "video",
                    referenceId: id, referenceType: CustomerVideoRules.ReferenceType);
            }
            catch
            {
                await DeletePendingAsync(id);
                throw;
            }

            if (!charge.Ok)
            {
                await DeletePendingAsync(id);
                return new(false, null, charge.Error ?? "Không trừ được điểm của khách hàng.");
            }
        }

        using (var conn = await _factory.OpenAsync())
        {
            await conn.ExecuteAsync("UPDATE studio.customer_videos SET charge_status=@status WHERE id=@id;",
                new { id, status = points > 0 ? CustomerVideoRules.StatusCharged : CustomerVideoRules.StatusFree });
        }

        return new(true, id, null);
    }

    /// <summary>Edits the content only; the charge is fixed when the video is added.</summary>
    public async Task<CustomerVideoSaveResult> UpdateAsync(CustomerVideo video)
    {
        var actor = RequireAdmin();
        if (string.IsNullOrWhiteSpace(video.Title)) return new(false, video.Id, "Vui lòng nhập tiêu đề video.");
        if (string.IsNullOrWhiteSpace(video.VideoUrl)) return new(false, video.Id, "Vui lòng tải video lên.");
        using var conn = await _factory.OpenAsync();
        var changed = await conn.ExecuteAsync("""
            UPDATE studio.customer_videos
               SET title=@title, video_url=@video, poster_url=@poster, note=@note, script=@script,
                   updated_at=now(), updated_by=@actor
             WHERE id=@id AND charge_status <> 'pending';
            """,
            new
            {
                id = video.Id, title = video.Title.Trim(), video = video.VideoUrl, poster = Blank(video.PosterUrl),
                note = Blank(video.Note), script = Blank(video.Script), actor = actor.UserId
            });
        return changed == 1 ? new(true, video.Id, null) : new(false, video.Id, "Không tìm thấy video.");
    }

    /// <summary>Removes a video; charged points go back to the customer's wallet first.</summary>
    public async Task<CustomerVideoSaveResult> DeleteAsync(Guid id)
    {
        var actor = RequireAdmin();
        var video = await GetAsync(id, actor);
        if (video is null) return new(false, id, "Không tìm thấy video.");
        if (video is { ChargeStatus: CustomerVideoRules.StatusCharged, ChargedPoints: > 0 })
        {
            var refund = await _wallets.RefundAsync(video.CustomerId, video.ChargedPoints,
                $"Hoàn {video.ChargedPoints:0.##} điểm: {CustomerVideoRules.DefaultName(video.ProductCode)} \"{video.Title}\"",
                "Xóa video khỏi tài khoản", actor, id);
            if (!refund.Ok) return new(false, id, refund.Error ?? "Không hoàn được điểm cho khách hàng.");
            using var mark = await _factory.OpenAsync();
            await mark.ExecuteAsync("UPDATE studio.customer_videos SET charge_status='refunded', updated_at=now(), updated_by=@actor WHERE id=@id;",
                new { id, actor = actor.UserId });
        }

        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM studio.customer_videos WHERE id=@id;", new { id });
        return new(true, id, null);
    }

    private async Task DeletePendingAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM studio.customer_videos WHERE id=@id AND charge_status='pending';", new { id });
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
