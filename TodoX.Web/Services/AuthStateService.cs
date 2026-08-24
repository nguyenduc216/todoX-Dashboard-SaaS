using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

/// <summary>
/// Holds the current user for the Blazor circuit and persists a lightweight marker
/// to the browser so the session survives circuit reconnects (fixes "kicked out" bug)
/// and, with Remember Me, survives browser restarts until logout.
/// The full session (incl. permissions) is re-hydrated from the DB on restore.
/// </summary>
public sealed class AuthStateService
{
    private const string StorageKey = "todox_auth";
    private const int CurrentMarkerVersion = 2;

    private readonly ProtectedLocalStorage _local;
    private readonly ProtectedSessionStorage _session;
    private readonly ILogger<AuthStateService> _logger;

    public AuthStateService(ProtectedLocalStorage local, ProtectedSessionStorage session, ILogger<AuthStateService> logger)
    {
        _local = local;
        _session = session;
        _logger = logger;
    }

    public CurrentUserSession? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser?.IsAuthenticated == true;

    /// <summary>True once a restore attempt from browser storage has completed.</summary>
    public bool IsInitialized { get; private set; }

    public event Action? OnChange;

    public void ResetInitializationForRetry()
    {
        IsInitialized = false;
        CurrentUser = null;
    }

    internal sealed record PersistedAuth(
        Guid UserId,
        bool Remember,
        Guid? ImpersonatorUserId = null,
        string? ImpersonatorDisplayName = null,
        int Version = CurrentMarkerVersion,
        DateTimeOffset? IssuedAtUtc = null);

    internal sealed record ResolvedPersistedAuth(PersistedAuth Marker, string Source);

    public async Task SignInAsync(CurrentUserSession user, bool rememberMe)
    {
        CurrentUser = user;
        var marker = CreateMarker(user, rememberMe);

        if (rememberMe)
        {
            var written = await PersistSignInMarkerAsync(_local.SetAsync(StorageKey, marker), "local", "write", user.UserId);
            if (written)
            {
                await ClearMarkerAsync("session", _session.DeleteAsync(StorageKey));
            }
            else
            {
                await ClearMarkerAsync("session", _session.DeleteAsync(StorageKey));
                _logger.LogWarning("AUTH_PERSIST_MARKER_INCOMPLETE intendedSource=local userId={UserId}", user.UserId);
            }
        }
        else
        {
            var written = await PersistSignInMarkerAsync(_session.SetAsync(StorageKey, marker), "session", "write", user.UserId);
            if (written)
            {
                await ClearMarkerAsync("local", _local.DeleteAsync(StorageKey));
            }
            else
            {
                await ClearMarkerAsync("local", _local.DeleteAsync(StorageKey));
                _logger.LogWarning("AUTH_PERSIST_MARKER_INCOMPLETE intendedSource=session userId={UserId}", user.UserId);
            }
        }

        NotifyStateChanged();
    }

    public async Task SignOutAsync()
    {
        CurrentUser = null;
        await ClearMarkerAsync("local", _local.DeleteAsync(StorageKey));
        await ClearMarkerAsync("session", _session.DeleteAsync(StorageKey));
        NotifyStateChanged();
    }

    public Task ImpersonateAsync(CurrentUserSession target, CurrentUserSession actor)
    {
        target.ImpersonatorUserId = actor.ImpersonatorUserId ?? actor.UserId;
        target.ImpersonatorDisplayName = actor.ImpersonatorDisplayName ?? actor.DisplayName;
        return SignInAsync(target, rememberMe: false);
    }

    public async Task<bool> StopImpersonationAsync(Func<Guid, Task<CurrentUserSession?>> rehydrate)
    {
        var originalUserId = CurrentUser?.ImpersonatorUserId;
        if (originalUserId is null)
        {
            return false;
        }

        var original = await rehydrate(originalUserId.Value);
        if (original is null)
        {
            await SignOutAsync();
            return false;
        }

        original.ImpersonatorUserId = null;
        original.ImpersonatorDisplayName = null;
        await SignInAsync(original, rememberMe: false);
        return true;
    }

    /// <summary>
    /// Attempt to restore the session from browser storage. Must be called from
    /// OnAfterRenderAsync (interactive) since it uses JS interop. Uses the provided
    /// re-hydrator to load a fresh session (with current permissions) from the DB.
    /// </summary>
    public async Task InitializeAsync(Func<Guid, Task<CurrentUserSession?>> rehydrate)
    {
        if (IsInitialized)
        {
            return;
        }

        try
        {
            var marker = await ReadMarkerAsync();
            if (marker is not null)
            {
                _logger.LogInformation(
                    "AUTH_RESTORE_MARKER_SELECTED source={Source} userId={UserId}",
                    marker.Source,
                    marker.Marker.UserId);

                var session = await rehydrate(marker.Marker.UserId);
                if (session is not null)
                {
                    if (marker.Marker.ImpersonatorUserId is Guid actorId)
                    {
                        session.ImpersonatorUserId = actorId;
                        session.ImpersonatorDisplayName = marker.Marker.ImpersonatorDisplayName;
                        if (string.IsNullOrWhiteSpace(session.ImpersonatorDisplayName))
                        {
                            var actor = await rehydrate(actorId);
                            session.ImpersonatorDisplayName = actor?.DisplayName;
                        }
                    }
                    CurrentUser = session;
                    _logger.LogInformation(
                        "AUTH_RESTORE_COMPLETED source={Source} userId={UserId} role={Role} isRoot={IsRoot} isAuthenticated={IsAuthenticated}",
                        marker.Source,
                        session.UserId,
                        session.Role,
                        session.IsRoot,
                        session.IsAuthenticated);
                }
                else
                {
                    // Account no longer valid; clear stale marker.
                    _logger.LogWarning(
                        "AUTH_RESTORE_REHYDRATE_FAILED source={Source} userId={UserId}; clearing persisted auth markers.",
                        marker.Source,
                        marker.Marker.UserId);
                    await ClearMarkerAsync("local", _local.DeleteAsync(StorageKey));
                    await ClearMarkerAsync("session", _session.DeleteAsync(StorageKey));
                }
            }
            else
            {
                _logger.LogInformation("AUTH_RESTORE_NO_MARKER");
            }
        }
        catch (Exception ex)
        {
            // Ignore storage/JS errors; treat as not authenticated.
            _logger.LogWarning(ex, "AUTH_RESTORE_FAILED");
        }
        finally
        {
            IsInitialized = true;
            _logger.LogInformation(
                "AUTH_RESTORE_STATE isInitialized={IsInitialized} userId={UserId} role={Role} isRoot={IsRoot} isAuthenticated={IsAuthenticated}",
                IsInitialized,
                CurrentUser?.UserId,
                CurrentUser?.Role,
                CurrentUser?.IsRoot,
                CurrentUser?.IsAuthenticated ?? false);
            NotifyStateChanged();
        }
    }

    private async Task<ResolvedPersistedAuth?> ReadMarkerAsync()
    {
        var local = await ReadMarkerFromStoreAsync("local", () => _local.GetAsync<PersistedAuth>(StorageKey));
        var session = await ReadMarkerFromStoreAsync("session", () => _session.GetAsync<PersistedAuth>(StorageKey));
        var selected = ResolveLatestMarker(local, session);

        if (selected is not null && local is not null && session is not null)
        {
            var skipped = selected.Source == "local" ? session : local;
            _logger.LogWarning(
                "AUTH_RESTORE_CONFLICT selectedSource={SelectedSource} skippedSource={SkippedSource} selectedUserId={SelectedUserId} skippedUserId={SkippedUserId}",
                selected.Source,
                skipped.Source,
                selected.Marker.UserId,
                skipped.Marker.UserId);
        }

        return selected;
    }

    private static PersistedAuth CreateMarker(CurrentUserSession user, bool rememberMe)
        => new(
            user.UserId,
            rememberMe,
            user.ImpersonatorUserId,
            user.ImpersonatorDisplayName,
            CurrentMarkerVersion,
            DateTimeOffset.UtcNow);

    internal static ResolvedPersistedAuth? ResolveLatestMarker(ResolvedPersistedAuth? local, ResolvedPersistedAuth? session)
    {
        if (local is null)
        {
            return session;
        }

        if (session is null)
        {
            return local;
        }

        var localIssuedAt = NormalizeIssuedAt(local.Marker);
        var sessionIssuedAt = NormalizeIssuedAt(session.Marker);

        if (localIssuedAt > sessionIssuedAt)
        {
            return local;
        }

        if (sessionIssuedAt > localIssuedAt)
        {
            return session;
        }

        return local.Marker.Remember == session.Marker.Remember
            ? session
            : session.Marker.Remember ? local : session;
    }

    private static DateTimeOffset NormalizeIssuedAt(PersistedAuth marker)
        => marker.IssuedAtUtc ?? DateTimeOffset.MinValue;

    private async Task<ResolvedPersistedAuth?> ReadMarkerFromStoreAsync(
        string source,
        Func<ValueTask<ProtectedBrowserStorageResult<PersistedAuth>>> read)
    {
        try
        {
            var result = await read();
            if (!result.Success || result.Value is null)
            {
                return null;
            }

            _logger.LogInformation(
                "AUTH_RESTORE_MARKER_FOUND source={Source} userId={UserId} version={Version} issuedAtUtc={IssuedAtUtc}",
                source,
                result.Value.UserId,
                result.Value.Version,
                result.Value.IssuedAtUtc);
            return new ResolvedPersistedAuth(result.Value, source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AUTH_RESTORE_MARKER_READ_FAILED source={Source}", source);
            return null;
        }
    }

    private async Task<bool> PersistSignInMarkerAsync(ValueTask storageTask, string source, string operation, Guid userId)
    {
        try
        {
            await storageTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AUTH_PERSIST_MARKER_FAILED operation={Operation} source={Source} userId={UserId}",
                operation,
                source,
                userId);
            return false;
        }
    }

    private async Task ClearMarkerAsync(string source, ValueTask storageTask)
    {
        try
        {
            await storageTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AUTH_CLEAR_MARKER_FAILED source={Source}", source);
        }
    }

    /// <summary>Update the cached display name after a profile edit.</summary>
    public void UpdateDisplayName(string displayName)
    {
        if (CurrentUser is not null)
        {
            CurrentUser.DisplayName = displayName;
            NotifyStateChanged();
        }
    }

    /// <summary>Update the cached avatar url after an avatar change so the top bar refreshes.</summary>
    public void UpdateAvatarUrl(string? avatarUrl)
    {
        if (CurrentUser is not null)
        {
            CurrentUser.AvatarUrl = avatarUrl;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
