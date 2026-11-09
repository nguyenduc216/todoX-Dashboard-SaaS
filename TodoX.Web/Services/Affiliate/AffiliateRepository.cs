using System.Data;
using System.Security.Cryptography;
using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Affiliate;

/// <summary>Pure rules for agent codes and commission amounts (unit-tested).</summary>
public static class AffiliateRules
{
    public const string AgentCodePrefix = "DL";
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string GenerateAgentCode()
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return AgentCodePrefix + new string(chars);
    }

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Hoa hồng = số tiền gói × tỷ lệ %, làm tròn tới đồng.</summary>
    public static decimal CalculateCommission(decimal baseAmount, decimal percent)
    {
        if (baseAmount <= 0 || percent <= 0)
        {
            return 0m;
        }

        var clamped = Math.Min(percent, 100m);
        return Math.Round(baseAmount * clamped / 100m, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Chu kỳ mới nối tiếp chu kỳ hiện tại nếu còn hạn, ngược lại bắt đầu từ ngày mua.</summary>
    public static DateTime ResolveCycleStart(DateTime? requested, DateTime? currentCycleEnd, DateTime purchasedAt)
    {
        if (requested is DateTime explicitStart)
        {
            return explicitStart.Date;
        }

        var purchaseDay = purchasedAt.Date;
        return currentCycleEnd is DateTime end && end.Date > purchaseDay ? end.Date : purchaseDay;
    }

    public static DateTime? ResolveCycleEnd(DateTime cycleStart, int cycleMonths)
        => cycleMonths > 0 ? cycleStart.Date.AddMonths(cycleMonths) : null;
}

/// <summary>
/// Đại lý, mã giới thiệu, gói khách hàng, lịch sử mua gói và hoa hồng (schema affiliate.*).
/// Tables come from database/manual/affiliate/20261104_affiliate_commission.sql (run manually).
/// Reads billing.token_transactions (top-ups) for top-up commissions; never writes point wallets / billing.
/// </summary>
public sealed class AffiliateRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AffiliateRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            return await conn.ExecuteScalarAsync<bool>(
                "SELECT to_regclass('affiliate.commissions') IS NOT NULL AND to_regclass('affiliate.customer_referrals') IS NOT NULL AND to_regclass('affiliate.payouts') IS NOT NULL;");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            return false;
        }
    }

    // ===================== Agents =====================

    public async Task<IReadOnlyList<AffiliateAgentView>> GetAgentsAsync() => await QueryAgentsAsync(null);

    /// <summary>Đại lý gắn với khách hàng đăng nhập (null nếu khách hàng không phải đại lý hoặc chưa có schema).</summary>
    public async Task<AffiliateAgentView?> GetAgentByCustomerAsync(Guid customerId)
    {
        if (!await IsReadyAsync())
        {
            return null;
        }

        return (await QueryAgentsAsync(customerId)).FirstOrDefault();
    }

    public async Task<bool> IsActiveAgentAsync(Guid customerId)
        => (await GetAgentByCustomerAsync(customerId))?.IsActive == true;

    /// <summary>Khách hàng do một đại lý giới thiệu (chỉ thông tin cơ bản, không lộ email/điện thoại).</summary>
    public async Task<IReadOnlyList<AffiliateReferredCustomer>> GetReferredCustomersAsync(Guid agentId)
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliateReferredCustomer>(
            """
            SELECT c.customer_code AS CustomerCode,
                   COALESCE(NULLIF(c.company_name, ''), c.full_name, '') AS CustomerName,
                   r.assigned_at AS ReferredAt,
                   (SELECT count(*) FROM affiliate.package_purchases p
                     WHERE p.customer_id = c.id AND p.status = 'paid')::int AS PurchaseCount,
                   (SELECT p.package_code FROM affiliate.package_purchases p
                     WHERE p.customer_id = c.id AND p.status = 'paid'
                     ORDER BY p.purchased_at DESC LIMIT 1) AS LatestPackageCode,
                   (SELECT max(p.cycle_end) FROM affiliate.package_purchases p
                     WHERE p.customer_id = c.id AND p.status = 'paid') AS CurrentCycleEnd
              FROM affiliate.customer_referrals r
              JOIN crm.customers c ON c.id = r.customer_id
             WHERE r.agent_id = @agentId
             ORDER BY r.assigned_at DESC;
            """, new { agentId });
        return rows.ToList();
    }

    /// <summary>Đại lý tự cập nhật tài khoản nhận hoa hồng. Chỉ sửa được bản ghi của chính khách hàng đó.</summary>
    public async Task<bool> UpdateOwnBankAsync(Guid customerId, string? bankName, string? accountNo, string? accountName, Guid? actorUserId)
    {
        using var conn = await _factory.OpenAsync();
        var updated = await conn.ExecuteAsync(
            """
            UPDATE affiliate.agents
               SET bank_name=@bankName, bank_account_no=@accountNo, bank_account_name=@accountName,
                   updated_at=now(), updated_by=@actorUserId
             WHERE customer_id=@customerId;
            """,
            new { customerId, bankName = Blank(bankName), accountNo = Blank(accountNo), accountName = Blank(accountName), actorUserId });
        return updated > 0;
    }

    private async Task<IReadOnlyList<AffiliateAgentView>> QueryAgentsAsync(Guid? customerId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliateAgentView>(
            """
            SELECT a.id AS Id, a.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   COALESCE(NULLIF(c.company_name, ''), c.full_name, '') AS CustomerName,
                   COALESCE(c.email, '') AS Email, COALESCE(c.phone, '') AS Phone,
                   a.agent_code AS AgentCode, a.is_active AS IsActive,
                   a.bank_name AS BankName, a.bank_account_no AS BankAccountNo,
                   a.bank_account_name AS BankAccountName, a.note AS Note, a.created_at AS CreatedAt,
                   a.topup_commission_percent AS TopupCommissionPercent,
                   (SELECT count(*) FROM affiliate.customer_referrals r WHERE r.agent_id = a.id)::int AS ReferredCount,
                   COALESCE((SELECT sum(m.commission_amount) FROM affiliate.commissions m
                              WHERE m.agent_id = a.id AND m.status = 'pending'), 0)
                 + COALESCE((SELECT sum(t.commission_amount) FROM affiliate.topup_commissions t
                              WHERE t.agent_id = a.id AND t.status = 'pending'), 0) AS PendingAmount,
                   COALESCE((SELECT sum(m.commission_amount) FROM affiliate.commissions m
                              WHERE m.agent_id = a.id AND m.status = 'paid'), 0)
                 + COALESCE((SELECT sum(t.commission_amount) FROM affiliate.topup_commissions t
                              WHERE t.agent_id = a.id AND t.status = 'paid'), 0) AS PaidAmount
              FROM affiliate.agents a
              JOIN crm.customers c ON c.id = a.customer_id
             WHERE a.tenant_id = @tenant AND (@customerId::uuid IS NULL OR a.customer_id = @customerId)
             ORDER BY a.is_active DESC, a.created_at DESC;
            """, new { tenant = _tenant.TenantId, customerId });
        return rows.ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, CustomerAffiliateInfo>> GetCustomerAffiliateMapAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CustomerAffiliateInfo>(
            """
            SELECT c.id AS CustomerId,
                   own.agent_code AS OwnAgentCode,
                   COALESCE(own.is_active, false) AS IsAgent,
                   ref_agent.agent_code AS ReferredByAgentCode,
                   COALESCE(NULLIF(ref_customer.company_name, ''), ref_customer.full_name) AS ReferredByAgentName
              FROM crm.customers c
              LEFT JOIN affiliate.agents own ON own.customer_id = c.id
              LEFT JOIN affiliate.customer_referrals r ON r.customer_id = c.id
              LEFT JOIN affiliate.agents ref_agent ON ref_agent.id = r.agent_id
              LEFT JOIN crm.customers ref_customer ON ref_customer.id = ref_agent.customer_id
             WHERE c.tenant_id = @tenant
               AND (own.id IS NOT NULL OR r.id IS NOT NULL);
            """, new { tenant = _tenant.TenantId });
        return rows.ToDictionary(x => x.CustomerId);
    }

    /// <summary>Bật/tắt đại lý cho khách hàng. Bật lần đầu sẽ sinh mã đại lý; tắt chỉ vô hiệu hóa (giữ lịch sử).</summary>
    public async Task<string?> SetCustomerAgentAsync(Guid customerId, bool isAgent, Guid? actorUserId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var existing = await conn.QuerySingleOrDefaultAsync<AgentRow>(
            "SELECT id AS Id, agent_code AS AgentCode, is_active AS IsActive FROM affiliate.agents WHERE customer_id=@customerId;",
            new { customerId });

        if (existing is not null)
        {
            var agent = existing;
            if (agent.IsActive != isAgent)
            {
                await conn.ExecuteAsync(
                    "UPDATE affiliate.agents SET is_active=@isAgent, updated_at=now(), updated_by=@actorUserId WHERE id=@id;",
                    new { id = agent.Id, isAgent, actorUserId });
            }

            return agent.AgentCode;
        }

        if (!isAgent)
        {
            return null;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = AffiliateRules.GenerateAgentCode();
            var inserted = await conn.ExecuteAsync(
                """
                INSERT INTO affiliate.agents (tenant_id, customer_id, agent_code, is_active, created_by, updated_by)
                SELECT @tenant, @customerId, @code, true, @actorUserId, @actorUserId
                 WHERE NOT EXISTS (SELECT 1 FROM affiliate.agents
                                    WHERE tenant_id=@tenant AND upper(agent_code)=@code)
                ON CONFLICT (customer_id) DO NOTHING;
                """,
                new { tenant = _tenant.TenantId, customerId, code, actorUserId });
            if (inserted > 0)
            {
                return code;
            }

            var raced = await conn.ExecuteScalarAsync<string?>(
                "SELECT agent_code FROM affiliate.agents WHERE customer_id=@customerId;", new { customerId });
            if (raced is not null)
            {
                return raced;
            }
        }

        throw new InvalidOperationException("Không sinh được mã đại lý duy nhất.");
    }

    public async Task UpdateAgentAsync(AffiliateAgentView agent, Guid? actorUserId)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE affiliate.agents
               SET bank_name=@BankName, bank_account_no=@BankAccountNo, bank_account_name=@BankAccountName,
                   note=@Note, is_active=@IsActive, topup_commission_percent=@TopupPercent,
                   updated_at=now(), updated_by=@actorUserId
             WHERE id=@Id;
            """,
            new
            {
                agent.Id,
                TopupPercent = agent.TopupCommissionPercent is decimal percent ? Math.Clamp(percent, 0m, 100m) : (decimal?)null,
                BankName = Blank(agent.BankName),
                BankAccountNo = Blank(agent.BankAccountNo),
                BankAccountName = Blank(agent.BankAccountName),
                Note = Blank(agent.Note),
                agent.IsActive,
                actorUserId
            });
    }

    /// <summary>Tìm đại lý đang hoạt động theo mã. Trả về null nếu mã sai hoặc DB chưa có schema affiliate.</summary>
    public async Task<AgentRefRow?> FindActiveAgentAsync(string? agentCode)
    {
        var code = AffiliateRules.NormalizeCode(agentCode);
        if (code.Length == 0 || !await IsReadyAsync())
        {
            return null;
        }

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<AgentRefRow>(
            """
            SELECT id AS AgentId, customer_id AS AgentCustomerId
              FROM affiliate.agents
             WHERE tenant_id=@tenant AND upper(agent_code)=@code AND is_active
             LIMIT 1;
            """, new { tenant = _tenant.TenantId, code });
    }

    /// <summary>Gán (hoặc gỡ khi mã rỗng) đại lý giới thiệu cho một khách hàng.</summary>
    public async Task<(bool Ok, string Message)> AssignReferralAsync(Guid customerId, string? agentCode, string source, Guid? actorUserId)
    {
        await _tenant.EnsureLoadedAsync();
        var code = AffiliateRules.NormalizeCode(agentCode);
        using var conn = await _factory.OpenAsync();

        if (code.Length == 0)
        {
            var removed = await conn.ExecuteAsync(
                "DELETE FROM affiliate.customer_referrals WHERE customer_id=@customerId;", new { customerId });
            return (true, removed > 0 ? "Đã gỡ đại lý giới thiệu." : string.Empty);
        }

        var agent = await FindActiveAgentAsync(code);
        if (agent is null)
        {
            return (false, $"Mã đại lý \"{code}\" không tồn tại hoặc đã ngưng hoạt động.");
        }

        if (agent.AgentCustomerId == customerId)
        {
            return (false, "Khách hàng không thể tự giới thiệu chính mình.");
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO affiliate.customer_referrals (tenant_id, customer_id, agent_id, referral_code, source, assigned_by, assigned_at)
            VALUES (@tenant, @customerId, @agentId, @code, @source, @actorUserId, now())
            ON CONFLICT (customer_id) DO UPDATE
               SET agent_id = EXCLUDED.agent_id,
                   referral_code = EXCLUDED.referral_code,
                   source = EXCLUDED.source,
                   assigned_by = EXCLUDED.assigned_by,
                   assigned_at = now()
             WHERE affiliate.customer_referrals.agent_id IS DISTINCT FROM EXCLUDED.agent_id;
            """,
            new { tenant = _tenant.TenantId, customerId, agentId = agent.AgentId, code, source, actorUserId });
        return (true, $"Đã gán đại lý {code}.");
    }

    // ===================== Packages =====================

    public async Task<IReadOnlyList<AffiliatePackage>> GetPackagesAsync(bool activeOnly = false)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliatePackage>(
            """
            SELECT id AS Id, package_code AS PackageCode, package_name AS PackageName,
                   price_amount AS PriceAmount, currency AS Currency, cycle_months AS CycleMonths,
                   commission_percent AS CommissionPercent, description AS Description,
                   is_active AS IsActive, sort_order AS SortOrder
              FROM affiliate.packages
             WHERE tenant_id=@tenant AND (NOT @activeOnly OR is_active)
             ORDER BY sort_order, package_code;
            """, new { tenant = _tenant.TenantId, activeOnly });
        return rows.ToList();
    }

    public async Task<(bool Ok, string Message)> SavePackageAsync(AffiliatePackage p, Guid? actorUserId)
    {
        var code = AffiliateRules.NormalizeCode(p.PackageCode);
        if (code.Length == 0 || string.IsNullOrWhiteSpace(p.PackageName))
        {
            return (false, "Nhập mã gói và tên gói.");
        }

        if (p.PriceAmount < 0 || p.CycleMonths < 0)
        {
            return (false, "Giá gói và số tháng chu kỳ không được âm.");
        }

        if (p.CommissionPercent < 0 || p.CommissionPercent > 100)
        {
            return (false, "Tỷ lệ hoa hồng phải trong khoảng 0–100%.");
        }

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var duplicate = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM affiliate.packages WHERE tenant_id=@tenant AND upper(package_code)=@code AND id<>@id);",
            new { tenant = _tenant.TenantId, code, id = p.Id });
        if (duplicate)
        {
            return (false, $"Mã gói {code} đã tồn tại.");
        }

        var args = new
        {
            id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id,
            tenant = _tenant.TenantId,
            code,
            name = p.PackageName.Trim(),
            price = p.PriceAmount,
            currency = string.IsNullOrWhiteSpace(p.Currency) ? "VND" : p.Currency.Trim().ToUpperInvariant(),
            cycle = p.CycleMonths,
            percent = p.CommissionPercent,
            description = Blank(p.Description),
            active = p.IsActive,
            sort = p.SortOrder,
            actorUserId
        };

        if (p.Id == Guid.Empty)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO affiliate.packages
                    (id, tenant_id, package_code, package_name, price_amount, currency, cycle_months,
                     commission_percent, description, is_active, sort_order, created_by, updated_by)
                VALUES (@id, @tenant, @code, @name, @price, @currency, @cycle,
                        @percent, @description, @active, @sort, @actorUserId, @actorUserId);
                """, args);
            return (true, "Đã thêm gói.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE affiliate.packages
               SET package_code=@code, package_name=@name, price_amount=@price, currency=@currency,
                   cycle_months=@cycle, commission_percent=@percent, description=@description,
                   is_active=@active, sort_order=@sort, updated_at=now(), updated_by=@actorUserId
             WHERE id=@id AND tenant_id=@tenant;
            """, args);
        return (true, "Đã cập nhật gói. Tỷ lệ mới chỉ áp dụng cho các lần mua gói sau.");
    }

    public async Task<(bool Ok, string Message)> DeletePackageAsync(Guid packageId)
    {
        using var conn = await _factory.OpenAsync();
        var used = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM affiliate.package_purchases WHERE package_id=@packageId);", new { packageId });
        if (used)
        {
            await conn.ExecuteAsync(
                "UPDATE affiliate.packages SET is_active=false, updated_at=now() WHERE id=@packageId;", new { packageId });
            return (true, "Gói đã có lịch sử mua nên được chuyển sang ngưng hoạt động thay vì xóa.");
        }

        await conn.ExecuteAsync("DELETE FROM affiliate.packages WHERE id=@packageId;", new { packageId });
        return (true, "Đã xóa gói.");
    }

    // ===================== Purchases + commissions =====================

    /// <summary>
    /// Ghi nhận khách hàng mua/gia hạn gói. Nếu khách hàng có đại lý giới thiệu đang hoạt động,
    /// tự tạo bản ghi hoa hồng (trạng thái "chưa chuyển khoản") theo tỷ lệ của gói tại thời điểm mua.
    /// </summary>
    public async Task<(bool Ok, string Message)> RecordPurchaseAsync(AffiliatePurchaseRequest request, Guid? actorUserId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();

        var package = await conn.QuerySingleOrDefaultAsync<AffiliatePackage>(
            """
            SELECT id AS Id, package_code AS PackageCode, package_name AS PackageName, price_amount AS PriceAmount,
                   currency AS Currency, cycle_months AS CycleMonths, commission_percent AS CommissionPercent,
                   is_active AS IsActive
              FROM affiliate.packages
             WHERE id=@id AND tenant_id=@tenant;
            """, new { id = request.PackageId, tenant = _tenant.TenantId }, tx);
        if (package is null || !package.IsActive)
        {
            return (false, "Gói không tồn tại hoặc đã ngưng hoạt động.");
        }

        var amount = request.Amount ?? package.PriceAmount;
        if (amount < 0)
        {
            return (false, "Số tiền không được âm.");
        }

        // Khóa theo khách hàng để hai lần ghi nhận đồng thời không cùng nối vào một chu kỳ.
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtext(@key));",
            new { key = "affiliate-purchase:" + request.CustomerId }, tx);

        var currentCycleEnd = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT max(cycle_end) FROM affiliate.package_purchases WHERE customer_id=@customerId AND status='paid';",
            new { customerId = request.CustomerId }, tx);
        var cycleStart = AffiliateRules.ResolveCycleStart(request.CycleStart, currentCycleEnd, request.PurchasedAt);
        var cycleEnd = AffiliateRules.ResolveCycleEnd(cycleStart, package.CycleMonths);

        var purchaseId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO affiliate.package_purchases
                (id, tenant_id, customer_id, package_id, package_code, package_name, amount, currency,
                 purchased_at, cycle_start, cycle_end, status, note, created_by)
            VALUES (@purchaseId, @tenant, @customerId, @packageId, @code, @name, @amount, @currency,
                    @purchasedAt, @cycleStart::date, @cycleEnd::date, 'paid', @note, @actorUserId);
            """,
            new
            {
                purchaseId,
                tenant = _tenant.TenantId,
                customerId = request.CustomerId,
                packageId = package.Id,
                code = package.PackageCode,
                name = package.PackageName,
                amount,
                currency = package.Currency,
                purchasedAt = request.PurchasedAt.ToUniversalTime(),
                cycleStart = DateTime.SpecifyKind(cycleStart, DateTimeKind.Unspecified),
                cycleEnd = cycleEnd is DateTime e ? DateTime.SpecifyKind(e, DateTimeKind.Unspecified) : (DateTime?)null,
                note = Blank(request.Note),
                actorUserId
            }, tx);

        var agent = await conn.QuerySingleOrDefaultAsync<AgentRow>(
            """
            SELECT a.id AS Id, a.agent_code AS AgentCode, a.is_active AS IsActive
              FROM affiliate.customer_referrals r
              JOIN affiliate.agents a ON a.id = r.agent_id
             WHERE r.customer_id=@customerId AND a.is_active AND a.customer_id <> r.customer_id
             LIMIT 1;
            """, new { customerId = request.CustomerId }, tx);

        var message = $"Đã ghi nhận gói {package.PackageCode}.";
        if (agent is not null)
        {
            var a = agent;
            var commission = AffiliateRules.CalculateCommission(amount, package.CommissionPercent);
            await conn.ExecuteAsync(
                """
                INSERT INTO affiliate.commissions
                    (tenant_id, purchase_id, agent_id, customer_id, package_id, base_amount,
                     commission_percent, commission_amount, currency, status)
                VALUES (@tenant, @purchaseId, @agentId, @customerId, @packageId, @amount,
                        @percent, @commission, @currency, 'pending');
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    purchaseId,
                    agentId = a.Id,
                    customerId = request.CustomerId,
                    packageId = package.Id,
                    amount,
                    percent = package.CommissionPercent,
                    commission,
                    currency = package.Currency
                }, tx);
            message += $" Hoa hồng cho đại lý {a.AgentCode}: {commission:N0} {package.Currency}.";
        }
        else
        {
            message += " Khách hàng không có đại lý giới thiệu nên không phát sinh hoa hồng.";
        }

        tx.Commit();
        return (true, message);
    }

    public async Task<IReadOnlyList<AffiliatePurchaseView>> GetPurchasesAsync(Guid? customerId = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliatePurchaseView>(
            """
            SELECT p.id AS Id, p.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   COALESCE(NULLIF(c.company_name, ''), c.full_name, '') AS CustomerName,
                   p.package_code AS PackageCode, p.package_name AS PackageName, p.amount AS Amount,
                   p.currency AS Currency, p.purchased_at AS PurchasedAt, p.cycle_start AS CycleStart,
                   p.cycle_end AS CycleEnd, p.status AS Status, p.note AS Note,
                   a.agent_code AS AgentCode, m.commission_amount AS CommissionAmount, m.status AS CommissionStatus
              FROM affiliate.package_purchases p
              JOIN crm.customers c ON c.id = p.customer_id
              LEFT JOIN affiliate.commissions m ON m.purchase_id = p.id
              LEFT JOIN affiliate.agents a ON a.id = m.agent_id
             WHERE p.tenant_id=@tenant AND (@customerId::uuid IS NULL OR p.customer_id=@customerId)
             ORDER BY p.purchased_at DESC
             LIMIT 500;
            """, new { tenant = _tenant.TenantId, customerId });
        return rows.ToList();
    }

    /// <summary>Hủy lần mua gói; hoa hồng chưa chi trả sẽ bị hủy theo. Không hủy được nếu hoa hồng đã chuyển khoản.</summary>
    public async Task<(bool Ok, string Message)> CancelPurchaseAsync(Guid purchaseId)
    {
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        var commissionStatus = await conn.ExecuteScalarAsync<string?>(
            "SELECT status FROM affiliate.commissions WHERE purchase_id=@purchaseId FOR UPDATE;", new { purchaseId }, tx);
        if (commissionStatus == AffiliateCommissionStatuses.Paid)
        {
            return (false, "Hoa hồng của lần mua này đã chuyển khoản. Hãy chuyển hoa hồng về \"Chưa chuyển khoản\" trước khi hủy.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE affiliate.package_purchases SET status='cancelled' WHERE id=@purchaseId;
            UPDATE affiliate.commissions SET status='cancelled', updated_at=now() WHERE purchase_id=@purchaseId;
            """, new { purchaseId }, tx);
        tx.Commit();
        return (true, "Đã hủy lần mua gói.");
    }

    public async Task<IReadOnlyList<AffiliateCommissionView>> GetCommissionsAsync(Guid? agentId = null, string? status = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliateCommissionView>(
            """
            SELECT m.id AS Id, m.purchase_id AS PurchaseId, m.agent_id AS AgentId, a.agent_code AS AgentCode,
                   COALESCE(NULLIF(ac.company_name, ''), ac.full_name, '') AS AgentName,
                   a.bank_name AS BankName, a.bank_account_no AS BankAccountNo, a.bank_account_name AS BankAccountName,
                   c.customer_code AS CustomerCode, COALESCE(NULLIF(c.company_name, ''), c.full_name, '') AS CustomerName,
                   p.package_code AS PackageCode, p.package_name AS PackageName, p.purchased_at AS PurchasedAt,
                   m.base_amount AS BaseAmount, m.commission_percent AS CommissionPercent,
                   m.commission_amount AS CommissionAmount, m.currency AS Currency, m.status AS Status,
                   m.paid_at AS PaidAt, m.bank_reference AS BankReference, m.note AS Note, m.created_at AS CreatedAt
              FROM affiliate.commissions m
              JOIN affiliate.agents a ON a.id = m.agent_id
              JOIN crm.customers ac ON ac.id = a.customer_id
              JOIN crm.customers c ON c.id = m.customer_id
              JOIN affiliate.package_purchases p ON p.id = m.purchase_id
             WHERE m.tenant_id=@tenant
               AND (@agentId::uuid IS NULL OR m.agent_id=@agentId)
               AND (@status::text IS NULL OR m.status=@status)
             ORDER BY m.created_at DESC
             LIMIT 1000;
            """, new { tenant = _tenant.TenantId, agentId, status = string.IsNullOrWhiteSpace(status) ? null : status });
        return rows.ToList();
    }

    /// <summary>
    /// Thanh toán các khoản hoa hồng gói đang chọn: mỗi đại lý một lần thanh toán (có lịch sử thanh toán).
    /// </summary>
    public async Task<int> MarkCommissionsPaidAsync(IReadOnlyCollection<Guid> ids, DateTime paidAt, string? bankReference, string? note, Guid? actorUserId)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        List<Guid> agentIds;
        using (var conn = await _factory.OpenAsync())
        {
            agentIds = (await conn.QueryAsync<Guid>(
                "SELECT DISTINCT agent_id FROM affiliate.commissions WHERE id = ANY(@ids) AND status='pending';",
                new { ids = ids.ToArray() })).ToList();
        }

        var paid = 0;
        foreach (var agentId in agentIds)
        {
            var result = await CreatePayoutAsync(agentId, paidAt, bankReference, note, actorUserId, packageCommissionIds: ids, topupCommissionIds: Array.Empty<Guid>());
            if (result.Ok) paid += result.ItemCount;
        }

        return paid;
    }

    // ===================== Hoa hồng nạp điểm & thanh toán =====================

    public async Task<AffiliateSettings> GetSettingsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<AffiliateSettings>(
            """
            SELECT vnd_per_point AS VndPerPoint, default_topup_commission_percent AS DefaultTopupCommissionPercent, updated_at AS UpdatedAt
              FROM affiliate.settings WHERE tenant_id=@tenant;
            """, new { tenant = _tenant.TenantId }) ?? new AffiliateSettings();
    }

    public async Task<(bool Ok, string Message)> SaveSettingsAsync(AffiliateSettings settings, Guid? actorUserId)
    {
        if (settings.VndPerPoint < 0) return (false, "Giá trị 1 điểm không hợp lệ.");
        if (settings.DefaultTopupCommissionPercent is < 0 or > 100) return (false, "Tỷ lệ hoa hồng phải từ 0 đến 100%.");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO affiliate.settings (tenant_id, vnd_per_point, default_topup_commission_percent, updated_at, updated_by)
            VALUES (@tenant, @vnd, @percent, now(), @actorUserId)
            ON CONFLICT (tenant_id) DO UPDATE
               SET vnd_per_point=EXCLUDED.vnd_per_point,
                   default_topup_commission_percent=EXCLUDED.default_topup_commission_percent,
                   updated_at=now(), updated_by=EXCLUDED.updated_by;
            """,
            new { tenant = _tenant.TenantId, vnd = settings.VndPerPoint, percent = settings.DefaultTopupCommissionPercent, actorUserId });
        return (true, "Đã lưu cấu hình. Áp dụng cho các lần nạp điểm chưa được ghi nhận hoa hồng.");
    }

    /// <summary>
    /// Ghi nhận hoa hồng cho các lần nạp điểm (billing.token_transactions loại 'topup') của khách được giới thiệu,
    /// kể từ lúc khách gắn với đại lý. Chỉ đọc billing; mỗi giao dịch nạp sinh tối đa một dòng.
    /// Chưa cấu hình giá trị 1 điểm thì chưa ghi nhận gì.
    /// </summary>
    public async Task<int> SyncTopupCommissionsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteAsync(
            """
            INSERT INTO affiliate.topup_commissions
                (tenant_id, agent_id, customer_id, transaction_id, topup_at, points, vnd_per_point,
                 base_amount, commission_percent, commission_amount)
            SELECT @tenant, a.id, r.customer_id, t.id, t.created_at, t.amount, s.vnd_per_point,
                   round(t.amount * s.vnd_per_point, 0),
                   COALESCE(a.topup_commission_percent, s.default_topup_commission_percent),
                   round(t.amount * s.vnd_per_point * COALESCE(a.topup_commission_percent, s.default_topup_commission_percent) / 100, 0)
              FROM billing.token_transactions t
              JOIN billing.token_wallets w ON w.id = t.wallet_id
              JOIN affiliate.customer_referrals r ON r.customer_id = w.customer_id
              JOIN affiliate.agents a ON a.id = r.agent_id AND a.is_active
              JOIN affiliate.settings s ON s.tenant_id = @tenant AND s.vnd_per_point > 0
             WHERE t.tenant_id = @tenant
               AND t.transaction_type = 'topup'
               AND t.amount > 0
               AND t.created_at >= r.assigned_at
               AND r.customer_id <> a.customer_id
               AND NOT EXISTS (SELECT 1 FROM affiliate.topup_commissions x WHERE x.transaction_id = t.id)
            ON CONFLICT (transaction_id) DO NOTHING;
            """, new { tenant = _tenant.TenantId });
    }

    public async Task<IReadOnlyList<AffiliateTopupCommissionView>> GetTopupCommissionsAsync(Guid? agentId = null, string? status = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliateTopupCommissionView>(
            """
            SELECT t.id AS Id, t.agent_id AS AgentId, a.agent_code AS AgentCode,
                   COALESCE(NULLIF(ac.company_name, ''), ac.full_name, '') AS AgentName,
                   c.customer_code AS CustomerCode, COALESCE(NULLIF(c.company_name, ''), c.full_name, '') AS CustomerName,
                   t.topup_at AS TopupAt, t.points AS Points, t.vnd_per_point AS VndPerPoint, t.base_amount AS BaseAmount,
                   t.commission_percent AS CommissionPercent, t.commission_amount AS CommissionAmount,
                   t.currency AS Currency, t.status AS Status, t.payout_id AS PayoutId, t.paid_at AS PaidAt
              FROM affiliate.topup_commissions t
              JOIN affiliate.agents a ON a.id = t.agent_id
              JOIN crm.customers ac ON ac.id = a.customer_id
              JOIN crm.customers c ON c.id = t.customer_id
             WHERE t.tenant_id=@tenant
               AND (@agentId::uuid IS NULL OR t.agent_id=@agentId)
               AND (@status::text IS NULL OR t.status=@status)
             ORDER BY t.topup_at DESC
             LIMIT 1000;
            """, new { tenant = _tenant.TenantId, agentId, status = string.IsNullOrWhiteSpace(status) ? null : status });
        return rows.ToList();
    }

    public async Task<AffiliatePendingSummary> GetPendingSummaryAsync(Guid agentId)
    {
        using var conn = await _factory.OpenAsync();
        var row = await conn.QuerySingleAsync<(int PackageCount, decimal PackageAmount, int TopupCount, decimal TopupAmount)>(
            """
            SELECT (SELECT count(*) FROM affiliate.commissions WHERE agent_id=@agentId AND status='pending')::int,
                   COALESCE((SELECT sum(commission_amount) FROM affiliate.commissions WHERE agent_id=@agentId AND status='pending'), 0),
                   (SELECT count(*) FROM affiliate.topup_commissions WHERE agent_id=@agentId AND status='pending')::int,
                   COALESCE((SELECT sum(commission_amount) FROM affiliate.topup_commissions WHERE agent_id=@agentId AND status='pending'), 0);
            """, new { agentId });
        return new AffiliatePendingSummary(row.PackageCount, row.PackageAmount, row.TopupCount, row.TopupAmount);
    }

    /// <summary>
    /// TodoX thanh toán hoa hồng cho một đại lý: các khoản chưa thanh toán (gói + nạp điểm, hoặc chỉ các khoản chỉ định)
    /// chuyển sang "đã chuyển khoản" và gắn với một dòng lịch sử thanh toán (chụp lại tài khoản ngân hàng của đại lý).
    /// </summary>
    public async Task<(bool Ok, string Message, int ItemCount, decimal Amount)> CreatePayoutAsync(
        Guid agentId, DateTime paidAt, string? bankReference, string? note, Guid? actorUserId,
        IReadOnlyCollection<Guid>? packageCommissionIds = null, IReadOnlyCollection<Guid>? topupCommissionIds = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        var agent = await conn.QuerySingleOrDefaultAsync<AgentBankRow>(
            """
            SELECT bank_name AS BankName, bank_account_no AS BankAccountNo, bank_account_name AS BankAccountName
              FROM affiliate.agents WHERE id=@agentId AND tenant_id=@tenant FOR UPDATE;
            """,
            new { agentId, tenant = _tenant.TenantId }, tx);
        if (agent is null)
        {
            tx.Rollback();
            return (false, "Không tìm thấy đại lý.", 0, 0);
        }

        var payoutId = Guid.NewGuid();
        var paidUtc = paidAt.ToUniversalTime();
        var reference = Blank(bankReference);
        // The payout row comes first so its items can reference it; amount and count are filled in below.
        await conn.ExecuteAsync(
            """
            INSERT INTO affiliate.payouts (id, tenant_id, agent_id, amount, item_count, paid_at, bank_name, bank_account_no,
                                           bank_account_name, bank_reference, note, created_by)
            VALUES (@payoutId, @tenant, @agentId, 0, 0, @paidUtc, @bankName, @accountNo, @accountName, @reference, @note, @actorUserId);
            """,
            new
            {
                payoutId, tenant = _tenant.TenantId, agentId, paidUtc, bankName = agent.BankName, accountNo = agent.BankAccountNo,
                accountName = agent.BankAccountName, reference, note = Blank(note), actorUserId
            }, tx);
        var packageAmounts = (await conn.QueryAsync<decimal>(
            """
            UPDATE affiliate.commissions
               SET status='paid', paid_at=@paidUtc, paid_by=@actorUserId, bank_reference=@reference,
                   payout_id=@payoutId, updated_at=now()
             WHERE agent_id=@agentId AND status='pending' AND (@ids::uuid[] IS NULL OR id = ANY(@ids))
            RETURNING commission_amount;
            """, new { agentId, paidUtc, actorUserId, reference, payoutId, ids = packageCommissionIds?.ToArray() }, tx)).ToList();
        var topupAmounts = (await conn.QueryAsync<decimal>(
            """
            UPDATE affiliate.topup_commissions
               SET status='paid', paid_at=@paidUtc, payout_id=@payoutId
             WHERE agent_id=@agentId AND status='pending' AND (@ids::uuid[] IS NULL OR id = ANY(@ids))
            RETURNING commission_amount;
            """, new { agentId, paidUtc, payoutId, ids = topupCommissionIds?.ToArray() }, tx)).ToList();
        var count = packageAmounts.Count + topupAmounts.Count;
        if (count == 0)
        {
            tx.Rollback();
            return (false, "Đại lý không có khoản hoa hồng nào chưa thanh toán.", 0, 0);
        }

        var amount = packageAmounts.Sum() + topupAmounts.Sum();
        await conn.ExecuteAsync("UPDATE affiliate.payouts SET amount=@amount, item_count=@count WHERE id=@payoutId;",
            new { amount, count, payoutId }, tx);
        tx.Commit();
        return (true, $"Đã ghi nhận thanh toán {amount:N0} VND ({count} khoản).", count, amount);
    }

    public async Task<IReadOnlyList<AffiliatePayoutView>> GetPayoutsAsync(Guid? agentId = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AffiliatePayoutView>(
            """
            SELECT p.id AS Id, p.agent_id AS AgentId, a.agent_code AS AgentCode,
                   COALESCE(NULLIF(ac.company_name, ''), ac.full_name, '') AS AgentName,
                   p.amount AS Amount, p.currency AS Currency, p.item_count AS ItemCount, p.paid_at AS PaidAt,
                   p.bank_name AS BankName, p.bank_account_no AS BankAccountNo, p.bank_account_name AS BankAccountName,
                   p.bank_reference AS BankReference, p.note AS Note, p.status AS Status, p.created_at AS CreatedAt
              FROM affiliate.payouts p
              JOIN affiliate.agents a ON a.id = p.agent_id
              JOIN crm.customers ac ON ac.id = a.customer_id
             WHERE p.tenant_id=@tenant AND (@agentId::uuid IS NULL OR p.agent_id=@agentId)
             ORDER BY p.paid_at DESC, p.created_at DESC
             LIMIT 500;
            """, new { tenant = _tenant.TenantId, agentId });
        return rows.ToList();
    }

    /// <summary>Hủy một lần thanh toán ghi nhầm: các khoản của nó quay về "chưa chuyển khoản".</summary>
    public async Task<(bool Ok, string Message)> CancelPayoutAsync(Guid payoutId, Guid? actorUserId)
    {
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        var cancelled = await conn.ExecuteAsync(
            "UPDATE affiliate.payouts SET status='cancelled', cancelled_at=now(), cancelled_by=@actorUserId WHERE id=@payoutId AND status='paid';",
            new { payoutId, actorUserId }, tx);
        if (cancelled == 0)
        {
            tx.Rollback();
            return (false, "Lần thanh toán không tồn tại hoặc đã hủy.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE affiliate.commissions
               SET status='pending', paid_at=NULL, paid_by=NULL, bank_reference=NULL, payout_id=NULL, updated_at=now()
             WHERE payout_id=@payoutId AND status='paid';
            """, new { payoutId }, tx);
        await conn.ExecuteAsync(
            "UPDATE affiliate.topup_commissions SET status='pending', paid_at=NULL, payout_id=NULL WHERE payout_id=@payoutId AND status='paid';",
            new { payoutId }, tx);
        tx.Commit();
        return (true, "Đã hủy lần thanh toán, các khoản hoa hồng quay về chưa chuyển khoản.");
    }

    private sealed class AgentBankRow
    {
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? BankAccountName { get; set; }
    }

    public sealed class AgentRefRow
    {
        public Guid AgentId { get; set; }
        public Guid AgentCustomerId { get; set; }
    }

    private sealed class AgentRow
    {
        public Guid Id { get; set; }
        public string AgentCode { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
