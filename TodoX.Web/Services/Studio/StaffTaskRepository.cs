using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.CustomerVideos;

namespace TodoX.Web.Services.Studio;

/// <summary>
/// studio.staff_tasks — database/manual/studio/20261119_staff_tasks.sql.
/// Every read and write checks the caller's staff_tasks permission here, not only in the page.
/// </summary>
public sealed class StaffTaskRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly AuthStateService _auth;
    private readonly CustomerVideoMediaService _media;
    private readonly ILogger<StaffTaskRepository> _logger;

    public StaffTaskRepository(TodoXConnectionFactory factory, TenantContext tenant, AuthStateService auth,
        CustomerVideoMediaService media, ILogger<StaffTaskRepository> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _auth = auth;
        _media = media;
        _logger = logger;
    }

    public const string Script = "database/manual/studio/20261119_staff_tasks.sql";
    public const string ProductsScript = "database/manual/studio/20261119_staff_task_products.sql";
    public const int MaxProducts = 20;
    private bool? _productsReady;

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            var ready = await conn.QuerySingleAsync<(bool Tasks, bool Products)>(
                "SELECT to_regclass('studio.staff_tasks') IS NOT NULL, to_regclass('studio.staff_task_products') IS NOT NULL;");
            _productsReady = ready.Products;
            return ready.Tasks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff task schema check failed.");
            return false;
        }
    }

    private CurrentUserSession Require(string permission)
    {
        var user = _auth.CurrentUser;
        if (user?.IsAuthenticated != true || user.IsCustomer
            || !(user.Can(permission) || AdminEndpointAuthorization.IsAdmin(user)))
            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");
        return user;
    }

    private const string Select =
        """
        SELECT t.id AS Id, t.title AS Title, t.note AS Note,
               t.customer_id AS CustomerId, COALESCE(NULLIF(c.company_name, ''), c.full_name) AS CustomerName,
               t.assignee_user_id AS AssigneeUserId, COALESCE(a.full_name, a.display_name, a.username) AS AssigneeName,
               t.start_at AS StartAt, t.completed_at AS CompletedAt, t.status AS Status,
               COALESCE(cb.full_name, cb.display_name, cb.username) AS CreatedByName,
               t.created_at AS CreatedAt, t.updated_at AS UpdatedAt
          FROM studio.staff_tasks t
          LEFT JOIN crm.customers c ON c.id = t.customer_id
          LEFT JOIN auth.app_users a ON a.id = t.assignee_user_id
          LEFT JOIN auth.app_users cb ON cb.id = t.created_by
        """;

    public async Task<IReadOnlyList<StaffTask>> ListAsync()
    {
        Require("staff_tasks.view");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = (await conn.QueryAsync<StaffTask>(
            Select + """
                      WHERE t.tenant_id = @tenant
                      ORDER BY CASE WHEN t.status IN ('done', 'cancelled') THEN 1 ELSE 0 END,
                               t.start_at NULLS LAST, t.created_at DESC;
                     """,
            new { tenant = _tenant.TenantId })).ToList();
        foreach (var row in rows) ToLocal(row);
        if (rows.Count > 0 && await ProductsReadyAsync(conn))
        {
            var products = await conn.QueryAsync<(Guid TaskId, string VideoUrl, string? Title, string? Source, string? PosterUrl)>(
                """
                SELECT p.task_id, p.video_url, p.title, p.source, p.poster_url
                  FROM studio.staff_task_products p
                  JOIN studio.staff_tasks t ON t.id = p.task_id
                 WHERE t.tenant_id = @tenant
                 ORDER BY p.task_id, p.sort_order, p.created_at;
                """, new { tenant = _tenant.TenantId });
            var byTask = rows.ToDictionary(x => x.Id);
            foreach (var p in products)
                if (byTask.TryGetValue(p.TaskId, out var task))
                    task.Products.Add(new StaffTaskProduct(p.VideoUrl, p.Title, p.Source, p.PosterUrl));
        }
        return rows;
    }

    /// <summary>True once 20261119_staff_task_products.sql has run (the page works without it, minus "Sản phẩm").</summary>
    public bool ProductsEnabled => _productsReady == true;

    private async Task<bool> ProductsReadyAsync(System.Data.IDbConnection conn)
        => _productsReady ??= await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('studio.staff_task_products') IS NOT NULL;");

    /// <summary>
    /// The uploaded video library (Video khách hàng, Kho mẫu video), newest first, each file named after the customer
    /// video or sample that uses it, so a task's product can be picked without guessing file names.
    /// </summary>
    public async Task<IReadOnlyList<StaffTaskProduct>> ListLibraryAsync()
    {
        var files = _media.ListLibrary(video: true, take: 400, "staff_tasks.create", "staff_tasks.update");
        if (files.Count == 0) return [];
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var urls = files.Select(f => f.Url).ToArray();
        var names = new Dictionary<string, (string Title, string? Poster)>(StringComparer.OrdinalIgnoreCase);
        async Task AddAsync(string sql)
        {
            try
            {
                foreach (var row in await conn.QueryAsync<(string Url, string Title, string? Poster)>(sql, new { urls, tenant = _tenant.TenantId }))
                    names.TryAdd(row.Url, (row.Title, row.Poster));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Staff task library: title lookup failed."); }
        }
        await AddAsync(LibraryCustomerVideosSql);
        await AddAsync(LibrarySamplesSql);
        await AddAsync(LibrarySampleVersionsSql);
        return files.Select(f => names.TryGetValue(f.Url, out var n)
                ? new StaffTaskProduct(f.Url, n.Title, f.Source, n.Poster) { UploadedAt = VietnamTime.FromUtc(f.UploadedAtUtc) }
                : new StaffTaskProduct(f.Url, null, f.Source, null) { UploadedAt = VietnamTime.FromUtc(f.UploadedAtUtc) })
            .ToList();
    }

    private const string LibraryCustomerVideosSql = """
        SELECT v.video_url, v.title, v.poster_url FROM studio.customer_videos v
          JOIN crm.customers c ON c.id = v.customer_id
         WHERE c.tenant_id = @tenant AND v.video_url = ANY(@urls) ORDER BY v.updated_at DESC;
        """;

    private const string LibrarySamplesSql =
        "SELECT s.video_url, s.title, s.poster_url FROM landing.video_samples s WHERE s.video_url = ANY(@urls);";

    private const string LibrarySampleVersionsSql = """
        SELECT x.video_url, s.title || ' (bản cũ)', COALESCE(x.poster_url, s.poster_url)
          FROM landing.video_sample_versions x JOIN landing.video_samples s ON s.id = x.sample_id
         WHERE x.video_url = ANY(@urls);
        """;

    public async Task<(bool Ok, string? Error)> SaveAsync(StaffTask task)
    {
        var isNew = task.Id == Guid.Empty;
        var user = Require(isNew ? "staff_tasks.create" : "staff_tasks.update");
        task.Title = task.Title?.Trim() ?? string.Empty;
        task.CompletedAt = StaffTaskRules.CompletedAtFor(task.Status, task.CompletedAt, VietnamTime.Now);
        var error = StaffTaskRules.Validate(task);
        if (error is not null) return (false, error);

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        if (task.CustomerId is { } customerId && !await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM crm.customers WHERE id = @id AND tenant_id = @tenant);",
                new { id = customerId, tenant = _tenant.TenantId }))
            return (false, "Khách hàng không tồn tại.");
        if (task.AssigneeUserId is { } assigneeId && !await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM auth.app_users WHERE id = @id AND tenant_id = @tenant);",
                new { id = assigneeId, tenant = _tenant.TenantId }))
            return (false, "Người thực hiện không tồn tại.");

        var args = new
        {
            id = isNew ? Guid.NewGuid() : task.Id,
            tenant = _tenant.TenantId,
            title = task.Title,
            note = string.IsNullOrWhiteSpace(task.Note) ? null : task.Note.Trim(),
            customerId = task.CustomerId,
            assignee = task.AssigneeUserId,
            startAt = VietnamTime.ToUtc(task.StartAt),
            completedAt = VietnamTime.ToUtc(task.CompletedAt),
            status = task.Status,
            by = user.UserId
        };
        if (isNew)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO studio.staff_tasks
                    (id, tenant_id, title, note, customer_id, assignee_user_id, start_at, completed_at, status,
                     created_at, created_by, updated_at, updated_by)
                VALUES (@id, @tenant, @title, @note, @customerId, @assignee, @startAt, @completedAt, @status,
                        now(), @by, now(), @by);
                """, args);
            task.Id = args.id;
        }
        else
        {
            var updated = await conn.ExecuteAsync(
                """
                UPDATE studio.staff_tasks
                   SET title = @title, note = @note, customer_id = @customerId, assignee_user_id = @assignee,
                       start_at = @startAt, completed_at = @completedAt, status = @status,
                       updated_at = now(), updated_by = @by
                 WHERE id = @id AND tenant_id = @tenant;
                """, args);
            if (updated == 0) return (false, "Công việc không còn tồn tại.");
        }
        if (await ProductsReadyAsync(conn)) await SaveProductsAsync(conn, args.id, task.Products, user.UserId);
        return (true, null);
    }

    private static async Task SaveProductsAsync(System.Data.IDbConnection conn, Guid taskId, IEnumerable<StaffTaskProduct> products, Guid? by)
    {
        var list = products
            .Where(p => StaffTaskRules.IsLibraryUrl(p.VideoUrl))
            .DistinctBy(p => p.VideoUrl, StringComparer.OrdinalIgnoreCase)
            .Take(MaxProducts)
            .Select((p, i) => new
            {
                id = Guid.NewGuid(), taskId, url = p.VideoUrl, title = p.Title, source = p.Source,
                poster = StaffTaskRules.IsLibraryUrl(p.PosterUrl) ? p.PosterUrl : null, sort = i, by
            })
            .ToList();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("DELETE FROM studio.staff_task_products WHERE task_id = @taskId;", new { taskId }, tx);
        if (list.Count > 0)
            await conn.ExecuteAsync(
                """
                INSERT INTO studio.staff_task_products (id, task_id, video_url, title, source, poster_url, sort_order, created_at, created_by)
                VALUES (@id, @taskId, @url, @title, @source, @poster, @sort, now(), @by);
                """, list, tx);
        tx.Commit();
    }

    /// <summary>Quick status change from the list; "Đã hoàn thành" stamps the completion time when it is empty.</summary>
    public async Task SetStatusAsync(Guid id, string status)
    {
        var user = Require("staff_tasks.update");
        if (!StaffTaskStatuses.All.Contains(status)) throw new ArgumentException("Trạng thái không hợp lệ.", nameof(status));
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE studio.staff_tasks
               SET status = @status,
                   completed_at = CASE WHEN @status = 'done' THEN COALESCE(completed_at, now()) ELSE completed_at END,
                   updated_at = now(), updated_by = @by
             WHERE id = @id AND tenant_id = @tenant;
            """, new { id, status, by = user.UserId, tenant = _tenant.TenantId });
    }

    public async Task DeleteAsync(Guid id)
    {
        Require("staff_tasks.delete");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM studio.staff_tasks WHERE id = @id AND tenant_id = @tenant;",
            new { id, tenant = _tenant.TenantId });
    }

    private static void ToLocal(StaffTask task)
    {
        task.StartAt = VietnamTime.FromUtc(task.StartAt);
        task.CompletedAt = VietnamTime.FromUtc(task.CompletedAt);
        task.CreatedAt = VietnamTime.FromUtc(task.CreatedAt);
        task.UpdatedAt = VietnamTime.FromUtc(task.UpdatedAt);
    }
}
