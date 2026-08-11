using System.Text;
using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models.Landing;

namespace TodoX.Web.Services.Landing;

public sealed class LandingContactLeadRepository
{
    private readonly TodoXConnectionFactory _factory;

    public LandingContactLeadRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            return await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('landing.contact_leads') IS NOT NULL;");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<LandingContactLeadSummary> GetSummaryAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            return await conn.QuerySingleAsync<LandingContactLeadSummary>(
                """
                SELECT count(*) FILTER (WHERE status='new') AS NewCount,
                       count(*) FILTER (WHERE status='consulting') AS ConsultingCount,
                       count(*) FILTER (WHERE next_follow_up_at >= date_trunc('day', now())
                                            AND next_follow_up_at < date_trunc('day', now()) + interval '1 day'
                                            AND status NOT IN ('converted','not_suitable','closed')) AS FollowUpTodayCount,
                       count(*) FILTER (WHERE status='converted') AS ConvertedCount
                  FROM landing.contact_leads
                 WHERE is_deleted = false;
                """);
        }
        catch (PostgresException ex) when (IsMissingSchema(ex))
        {
            throw new LandingContactSchemaUnavailableException("Landing contact schema is not available.", ex);
        }
    }

    public async Task<LandingContactLeadPage> SearchAsync(LandingContactLeadFilter filter)
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            var (where, parameters) = BuildWhere(filter);
            parameters.Add("limit", Math.Clamp(filter.PageSize, 20, 100));
            parameters.Add("offset", Math.Max(0, filter.Page - 1) * Math.Clamp(filter.PageSize, 20, 100));

            var sql =
                $"""
                SELECT l.id AS Id, l.lead_code AS LeadCode, l.created_at AS CreatedAt,
                       l.full_name AS FullName, l.phone AS Phone, l.email AS Email,
                       l.company_name AS CompanyName, l.industry_code AS IndustryCode,
                       l.interested_product AS InterestedProduct, l.utm_source AS UtmSource,
                       l.utm_campaign AS UtmCampaign, l.status AS Status, l.priority AS Priority,
                       l.assigned_user_id AS AssignedUserId, u.display_name AS AssignedUserName,
                       l.next_follow_up_at AS NextFollowUpAt, l.is_deleted AS IsDeleted
                  FROM landing.contact_leads l
                  LEFT JOIN auth.app_users u ON u.id = l.assigned_user_id
                 {where}
                 ORDER BY l.created_at DESC
                 LIMIT @limit OFFSET @offset;

                SELECT count(*) FROM landing.contact_leads l {where};
                """;

            using var multi = await conn.QueryMultipleAsync(sql, parameters);
            return new LandingContactLeadPage
            {
                Items = (await multi.ReadAsync<LandingContactLeadListItem>()).ToList(),
                Total = await multi.ReadSingleAsync<long>()
            };
        }
        catch (PostgresException ex) when (IsMissingSchema(ex))
        {
            throw new LandingContactSchemaUnavailableException("Landing contact schema is not available.", ex);
        }
    }

    public async Task<LandingContactLeadDetailResult> GetDetailAsync(Guid id)
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            var lead = await conn.QuerySingleOrDefaultAsync<LandingContactLeadDetail>(
                """
                SELECT l.id AS Id, l.lead_code AS LeadCode, l.created_at AS CreatedAt,
                       l.full_name AS FullName, l.phone AS Phone, l.email AS Email,
                       l.company_name AS CompanyName, l.industry_code AS IndustryCode,
                       l.interested_product AS InterestedProduct, l.utm_source AS UtmSource,
                       l.utm_medium AS UtmMedium, l.utm_campaign AS UtmCampaign,
                       l.utm_content AS UtmContent, l.utm_term AS UtmTerm,
                       l.status AS Status, l.priority AS Priority,
                       l.assigned_user_id AS AssignedUserId, u.display_name AS AssignedUserName,
                       l.next_follow_up_at AS NextFollowUpAt, l.is_deleted AS IsDeleted,
                       l.message AS Message, l.source_url AS SourceUrl, l.referrer_url AS ReferrerUrl,
                       l.consent_accepted AS ConsentAccepted, l.consent_at AS ConsentAt,
                       l.internal_note AS InternalNote, l.first_contacted_at AS FirstContactedAt,
                       l.converted_at AS ConvertedAt, l.closed_at AS ClosedAt
                  FROM landing.contact_leads l
                  LEFT JOIN auth.app_users u ON u.id = l.assigned_user_id
                 WHERE l.id = @id;
                """, new { id });

            var activities = (await conn.QueryAsync<LandingContactLeadActivity>(
                """
                SELECT a.id AS Id, a.activity_type AS ActivityType, a.title AS Title, a.content AS Content,
                       a.old_status AS OldStatus, a.new_status AS NewStatus, a.activity_at AS ActivityAt,
                       a.created_by AS CreatedBy, u.display_name AS CreatedByName
                  FROM landing.contact_lead_activities a
                  LEFT JOIN auth.app_users u ON u.id = a.created_by
                 WHERE a.lead_id = @id
                 ORDER BY a.activity_at DESC;
                """, new { id })).ToList();

            return new LandingContactLeadDetailResult { Lead = lead, Activities = activities };
        }
        catch (PostgresException ex) when (IsMissingSchema(ex))
        {
            throw new LandingContactSchemaUnavailableException("Landing contact schema is not available.", ex);
        }
    }

    public async Task<IReadOnlyList<LandingStaffOption>> GetStaffAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<LandingStaffOption>(
            """
            SELECT id AS Id, COALESCE(NULLIF(display_name,''), NULLIF(full_name,''), email, username) AS DisplayName
              FROM auth.app_users
             WHERE is_active = true AND user_type IN ('root','admin')
             ORDER BY display_name, full_name, email;
            """);
        return rows.ToList();
    }

    public async Task UpdateAsync(Guid leadId, LandingLeadActionRequest request, Guid actorId)
    {
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        var before = await conn.QuerySingleAsync<(string Status, string Priority, Guid? AssignedUserId)>(
            "SELECT status, priority, assigned_user_id FROM landing.contact_leads WHERE id=@leadId FOR UPDATE;",
            new { leadId }, tx);

        var status = string.IsNullOrWhiteSpace(request.Status) ? before.Status : request.Status;
        var priority = string.IsNullOrWhiteSpace(request.Priority) ? before.Priority : request.Priority;

        await conn.ExecuteAsync(
            """
            UPDATE landing.contact_leads
               SET status = @status,
                   priority = @priority,
                   assigned_user_id = @assignedUserId,
                   next_follow_up_at = @nextFollowUpAt,
                   internal_note = COALESCE(@note, internal_note),
                   first_contacted_at = CASE WHEN @status IN ('contacted','consulting','quotation_sent','qualified','converted') AND first_contacted_at IS NULL THEN now() ELSE first_contacted_at END,
                   converted_at = CASE WHEN @status = 'converted' THEN COALESCE(converted_at, now()) ELSE converted_at END,
                   closed_at = CASE WHEN @status IN ('not_suitable','closed') THEN COALESCE(closed_at, now()) ELSE closed_at END,
                   updated_by = @actorId
             WHERE id = @leadId;
            """,
            new
            {
                leadId,
                status,
                priority,
                assignedUserId = request.AssignedUserId,
                nextFollowUpAt = request.NextFollowUpAt,
                note = NullIfBlank(request.Note),
                actorId
            }, tx);

        await InsertActivityAsync(conn, tx, leadId, request.ActivityType, ActivityTitle(request.ActivityType),
            request.Note, before.Status, status, before.AssignedUserId, request.AssignedUserId, actorId);
        tx.Commit();
    }

    public async Task SoftDeleteAsync(Guid leadId, Guid actorId, bool restore)
    {
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            restore
                ? "UPDATE landing.contact_leads SET is_deleted=false, deleted_at=NULL, deleted_by=NULL, updated_by=@actorId WHERE id=@leadId;"
                : "UPDATE landing.contact_leads SET is_deleted=true, deleted_at=now(), deleted_by=@actorId, updated_by=@actorId WHERE id=@leadId;",
            new { leadId, actorId }, tx);

        await InsertActivityAsync(conn, tx, leadId, restore ? "restored" : "deleted",
            restore ? "Khôi phục lead" : "Xóa mềm lead", null, null, null, null, null, actorId);
        tx.Commit();
    }

    public async Task<string> ExportCsvAsync(LandingContactLeadFilter filter)
    {
        var exportFilter = new LandingContactLeadFilter
        {
            Search = filter.Search,
            Status = filter.Status,
            Priority = filter.Priority,
            AssignedUserId = filter.AssignedUserId,
            Industry = filter.Industry,
            Need = filter.Need,
            Utm = filter.Utm,
            CreatedFrom = filter.CreatedFrom,
            CreatedTo = filter.CreatedTo,
            FollowUpDate = filter.FollowUpDate,
            IncludeDeleted = filter.IncludeDeleted,
            Page = 1,
            PageSize = 100
        };
        var page = await SearchAsync(exportFilter);
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("LeadCode,CreatedAt,FullName,Phone,Email,Company,Need,UtmSource,UtmCampaign,Status,Priority,AssignedUser,FollowUp");
        foreach (var item in page.Items)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(item.LeadCode),
                Csv(ToVietnamTime(item.CreatedAt).ToString("yyyy-MM-dd HH:mm")),
                Csv(item.FullName),
                Csv(item.Phone),
                Csv(item.Email),
                Csv(item.CompanyName),
                Csv(item.InterestedProduct),
                Csv(item.UtmSource),
                Csv(item.UtmCampaign),
                Csv(LandingLeadStatuses.Label(item.Status)),
                Csv(LandingLeadPriorities.Label(item.Priority)),
                Csv(item.AssignedUserName),
                Csv(item.NextFollowUpAt is null ? "" : ToVietnamTime(item.NextFollowUpAt.Value).ToString("yyyy-MM-dd HH:mm"))
            }));
        }
        return sb.ToString();
    }

    public static DateTimeOffset ToVietnamTime(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7));

    private static (string Where, DynamicParameters Parameters) BuildWhere(LandingContactLeadFilter filter)
    {
        var clauses = new List<string>();
        var p = new DynamicParameters();
        if (!filter.IncludeDeleted)
        {
            clauses.Add("l.is_deleted = false");
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            clauses.Add("(l.lead_code ILIKE @q OR l.full_name ILIKE @q OR l.phone ILIKE @q OR l.email ILIKE @q OR l.company_name ILIKE @q)");
            p.Add("q", $"%{filter.Search.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(filter.Status)) { clauses.Add("l.status = @status"); p.Add("status", filter.Status); }
        if (!string.IsNullOrWhiteSpace(filter.Priority)) { clauses.Add("l.priority = @priority"); p.Add("priority", filter.Priority); }
        if (filter.AssignedUserId is not null) { clauses.Add("l.assigned_user_id = @assignedUserId"); p.Add("assignedUserId", filter.AssignedUserId); }
        if (!string.IsNullOrWhiteSpace(filter.Industry)) { clauses.Add("l.industry_code = @industry"); p.Add("industry", filter.Industry); }
        if (!string.IsNullOrWhiteSpace(filter.Need)) { clauses.Add("l.interested_product = @need"); p.Add("need", filter.Need); }
        if (!string.IsNullOrWhiteSpace(filter.Utm)) { clauses.Add("(l.utm_source ILIKE @utm OR l.utm_medium ILIKE @utm OR l.utm_campaign ILIKE @utm)"); p.Add("utm", $"%{filter.Utm.Trim()}%"); }
        if (filter.CreatedFrom is not null) { clauses.Add("l.created_at >= @createdFrom"); p.Add("createdFrom", filter.CreatedFrom.Value); }
        if (filter.CreatedTo is not null) { clauses.Add("l.created_at < @createdTo"); p.Add("createdTo", filter.CreatedTo.Value.AddDays(1)); }
        if (filter.FollowUpDate is not null)
        {
            clauses.Add("l.next_follow_up_at >= @followFrom AND l.next_follow_up_at < @followTo");
            p.Add("followFrom", filter.FollowUpDate.Value.Date);
            p.Add("followTo", filter.FollowUpDate.Value.Date.AddDays(1));
        }
        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return (where, p);
    }

    private static async Task InsertActivityAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx,
        Guid leadId, string activityType, string title, string? content, string? oldStatus, string? newStatus,
        Guid? oldAssignedUserId, Guid? newAssignedUserId, Guid actorId)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO landing.contact_lead_activities
                (lead_id, activity_type, title, content, old_status, new_status,
                 old_assigned_user_id, new_assigned_user_id, created_by)
            VALUES
                (@leadId, @activityType, @title, @content, @oldStatus, @newStatus,
                 @oldAssignedUserId, @newAssignedUserId, @actorId);
            """,
            new { leadId, activityType, title, content = NullIfBlank(content), oldStatus, newStatus, oldAssignedUserId, newAssignedUserId, actorId }, tx);
    }

    private static bool IsMissingSchema(PostgresException ex) => ex.SqlState is "42P01" or "3F000" or "42703";

    private static string ActivityTitle(string type) => type switch
    {
        "status_changed" => "Cập nhật trạng thái",
        "assigned" => "Phân công phụ trách",
        "phone_call" => "Ghi nhận cuộc gọi",
        "meeting" => "Ghi nhận meeting",
        "quotation" => "Gửi báo giá",
        "converted" => "Đánh dấu chuyển đổi",
        "closed" => "Đóng lead",
        _ => "Thêm ghi chú"
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@".Contains(value[0]))
        {
            value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
