using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>What a customer's package gives right now: its name, when it ends and the EditVideo storage quota.</summary>
public sealed record PackageEntitlement(string? PackageCode, string? PackageName, DateTimeOffset? CycleEnd, long StorageBytes)
{
    public const long DefaultStorageBytes = 1024L * 1024 * 1024;
}

/// <summary>
/// Turns a bought package into what the customer gets: points in the wallet, the TodoX EditVideo license (issued or
/// extended to the end of the paid cycle) and the storage quota. The current package is the latest paid purchase
/// whose cycle has not ended; without one the customer keeps the default 1 GB.
/// </summary>
public sealed class PackageEntitlementService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly WalletService _wallet;
    private readonly EditVideoLicenseService _licenses;
    private readonly ILogger<PackageEntitlementService> _logger;

    public PackageEntitlementService(TodoXConnectionFactory factory, TenantContext tenant, WalletService wallet,
        EditVideoLicenseService licenses, ILogger<PackageEntitlementService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _wallet = wallet;
        _licenses = licenses;
        _logger = logger;
    }

    private sealed record PurchaseRow(Guid Id, string PackageCode, string PackageName, int PointsGranted, DateTime? CycleEnd);

    /// <summary>Applies the customer's latest paid purchase; returns what was done, for the admin's message.</summary>
    public async Task<string> ApplyLatestPurchaseAsync(Guid customerId, Guid? actorUserId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync();
        PurchaseRow? purchase;
        using (var conn = await _factory.OpenAsync(ct))
        {
            purchase = await conn.QuerySingleOrDefaultAsync<PurchaseRow>(
                """
                SELECT id AS Id, package_code AS PackageCode, package_name AS PackageName,
                       points_granted AS PointsGranted, cycle_end AS CycleEnd
                  FROM affiliate.package_purchases
                 WHERE customer_id = @customerId AND tenant_id = @tenant AND status = 'paid'
                 ORDER BY purchased_at DESC, created_at DESC
                 LIMIT 1;
                """, new { customerId, tenant = _tenant.TenantId });
        }

        if (purchase is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        try
        {
            var granted = await _wallet.GrantPackagePointsAsync(customerId, purchase.PointsGranted,
                $"Điểm gói {purchase.PackageName}", purchase.Id, actorUserId);
            if (granted is not null)
            {
                parts.Add($"Đã cộng {purchase.PointsGranted:N0} điểm vào ví.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Package points for purchase {PurchaseId} failed", purchase.Id);
            parts.Add("Chưa cộng được điểm của gói, hãy cộng tay trong Ví điểm.");
        }

        try
        {
            var (_, message) = await _licenses.EnsureForPackageAsync(customerId, purchase.PackageCode, EndOfDay(purchase.CycleEnd), actorUserId, ct);
            parts.Add(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EditVideo license for purchase {PurchaseId} failed", purchase.Id);
            parts.Add("Chưa cấp được license TodoX EditVideo, hãy cấp tay ở trang Khách hàng.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>The package in force today, or the defaults (no package, 1 GB).</summary>
    public async Task<PackageEntitlement> GetAsync(Guid customerId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(string PackageCode, string PackageName, DateTime? CycleEnd, int? StorageMb)?>(
            """
            SELECT p.package_code, p.package_name, p.cycle_end, COALESCE(p.storage_mb, k.storage_mb)
              FROM affiliate.package_purchases p
              LEFT JOIN affiliate.packages k ON k.id = p.package_id
             WHERE p.customer_id = @customerId AND p.status = 'paid'
               AND (p.cycle_end IS NULL OR p.cycle_end >= current_date)
             ORDER BY p.purchased_at DESC
             LIMIT 1;
            """, new { customerId });
        if (row is not { } r)
        {
            return new PackageEntitlement(null, null, null, PackageEntitlement.DefaultStorageBytes);
        }

        var storage = r.StorageMb is > 0 ? r.StorageMb.Value * 1024L * 1024 : PackageEntitlement.DefaultStorageBytes;
        return new PackageEntitlement(r.PackageCode, r.PackageName, EndOfDay(r.CycleEnd), storage);
    }

    /// <summary>A cycle ending on a date runs to the end of that day, Vietnam time.</summary>
    internal static DateTimeOffset? EndOfDay(DateTime? date) =>
        date is { } d ? new DateTimeOffset(d.Date.AddDays(1).AddTicks(-1), TimeSpan.FromHours(7)) : null;
}
