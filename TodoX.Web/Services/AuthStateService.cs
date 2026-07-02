using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
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

    private readonly ProtectedLocalStorage _local;
    private readonly ProtectedSessionStorage _session;

    public AuthStateService(ProtectedLocalStorage local, ProtectedSessionStorage session)
    {
        _local = local;
        _session = session;
    }

    public CurrentUserSession? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser?.IsAuthenticated == true;

    /// <summary>True once a restore attempt from browser storage has completed.</summary>
    public bool IsInitialized { get; private set; }

    public event Action? OnChange;

    private sealed record PersistedAuth(Guid UserId, bool Remember);

    public async Task SignInAsync(CurrentUserSession user, bool rememberMe)
    {
        CurrentUser = user;
        var marker = new PersistedAuth(user.UserId, rememberMe);
        try
        {
            if (rememberMe)
            {
                await _local.SetAsync(StorageKey, marker);
                await _session.DeleteAsync(StorageKey);
            }
            else
            {
                await _session.SetAsync(StorageKey, marker);
                await _local.DeleteAsync(StorageKey);
            }
        }
        catch
        {
            // Storage may be unavailable during prerender; the in-memory session still works.
        }
        NotifyStateChanged();
    }

    public async Task SignOutAsync()
    {
        CurrentUser = null;
        try
        {
            await _local.DeleteAsync(StorageKey);
            await _session.DeleteAsync(StorageKey);
        }
        catch { /* ignore */ }
        NotifyStateChanged();
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
                var session = await rehydrate(marker.UserId);
                if (session is not null)
                {
                    CurrentUser = session;
                }
                else
                {
                    // Account no longer valid; clear stale marker.
                    await _local.DeleteAsync(StorageKey);
                    await _session.DeleteAsync(StorageKey);
                }
            }
        }
        catch
        {
            // Ignore storage/JS errors; treat as not authenticated.
        }
        finally
        {
            IsInitialized = true;
            NotifyStateChanged();
        }
    }

    private async Task<PersistedAuth?> ReadMarkerAsync()
    {
        var local = await _local.GetAsync<PersistedAuth>(StorageKey);
        if (local.Success && local.Value is not null)
        {
            return local.Value;
        }

        var session = await _session.GetAsync<PersistedAuth>(StorageKey);
        if (session.Success && session.Value is not null)
        {
            return session.Value;
        }

        return null;
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
