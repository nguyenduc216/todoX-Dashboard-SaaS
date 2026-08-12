using System.Reflection;
using System.Text;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AuthStatePersistenceTests
{
    [Fact]
    public void LatestSessionMarkerWinsOverStaleLocalMarker()
    {
        var localUserId = Guid.NewGuid();
        var sessionUserId = Guid.NewGuid();
        var issuedAt = DateTimeOffset.UtcNow;
        var local = Marker("local", localUserId, remember: true, issuedAt.AddMinutes(-5));
        var session = Marker("session", sessionUserId, remember: false, issuedAt);

        var selected = AuthStateService.ResolveLatestMarker(local, session);

        Assert.NotNull(selected);
        Assert.Equal("session", selected.Source);
        Assert.Equal(sessionUserId, selected.Marker.UserId);
    }

    [Fact]
    public void LatestLocalMarkerWinsOverStaleSessionMarker()
    {
        var localUserId = Guid.NewGuid();
        var sessionUserId = Guid.NewGuid();
        var issuedAt = DateTimeOffset.UtcNow;
        var local = Marker("local", localUserId, remember: true, issuedAt);
        var session = Marker("session", sessionUserId, remember: false, issuedAt.AddMinutes(-5));

        var selected = AuthStateService.ResolveLatestMarker(local, session);

        Assert.NotNull(selected);
        Assert.Equal("local", selected.Source);
        Assert.Equal(localUserId, selected.Marker.UserId);
    }

    [Fact]
    public void BrowserMarkerDoesNotPersistRoleRootOrPermissions()
    {
        var persistedProperties = typeof(AuthStateService.PersistedAuth)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(x => x.Name)
            .ToArray();

        Assert.Contains("UserId", persistedProperties);
        Assert.Contains("Version", persistedProperties);
        Assert.Contains("IssuedAtUtc", persistedProperties);
        Assert.DoesNotContain("Role", persistedProperties);
        Assert.DoesNotContain("IsRoot", persistedProperties);
        Assert.DoesNotContain("Permissions", persistedProperties);
        Assert.DoesNotContain("Password", persistedProperties);
    }

    [Fact]
    public void AuthStateServiceUsesRehydrateForRestoredUserAndLogsSanitizedDiagnostics()
    {
        var source = ReadSource("TodoX.Web", "Services", "AuthStateService.cs");

        Assert.Contains("var session = await rehydrate(marker.Marker.UserId);", source, StringComparison.Ordinal);
        Assert.Contains("AUTH_RESTORE_MARKER_SELECTED", source, StringComparison.Ordinal);
        Assert.Contains("AUTH_RESTORE_COMPLETED", source, StringComparison.Ordinal);
        Assert.Contains("AUTH_RESTORE_STATE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", source, StringComparison.Ordinal);
        Assert.DoesNotContain("protected marker payload", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignInRememberMeTrueWritesLocalAndClearsSessionMarker()
    {
        var source = ReadSource("TodoX.Web", "Services", "AuthStateService.cs");

        Assert.Contains("if (rememberMe)", source, StringComparison.Ordinal);
        Assert.Contains("PersistSignInMarkerAsync(_local.SetAsync(StorageKey, marker), \"local\", \"write\", user.UserId)", source, StringComparison.Ordinal);
        Assert.Contains("ClearMarkerAsync(\"session\", _session.DeleteAsync(StorageKey))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SignInRememberMeFalseWritesSessionAndClearsLocalMarker()
    {
        var source = ReadSource("TodoX.Web", "Services", "AuthStateService.cs");

        Assert.Contains("PersistSignInMarkerAsync(_session.SetAsync(StorageKey, marker), \"session\", \"write\", user.UserId)", source, StringComparison.Ordinal);
        Assert.Contains("ClearMarkerAsync(\"local\", _local.DeleteAsync(StorageKey))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SignOutClearsBothStores()
    {
        var source = ReadSource("TodoX.Web", "Services", "AuthStateService.cs");

        Assert.Contains("public async Task SignOutAsync()", source, StringComparison.Ordinal);
        Assert.Contains("ClearMarkerAsync(\"local\", _local.DeleteAsync(StorageKey))", source, StringComparison.Ordinal);
        Assert.Contains("ClearMarkerAsync(\"session\", _session.DeleteAsync(StorageKey))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountRehydrateKeepsLoginSessionSemantics()
    {
        var source = ReadSource("TodoX.Web", "Services", "AccountService.cs");

        Assert.Contains("return await BuildSessionAsync(row);", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(source, "return await BuildSessionAsync(row);"));
    }

    private static AuthStateService.ResolvedPersistedAuth Marker(
        string source,
        Guid userId,
        bool remember,
        DateTimeOffset issuedAt)
        => new(
            new AuthStateService.PersistedAuth(
                userId,
                remember,
                Version: 2,
                IssuedAtUtc: issuedAt),
            source);

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadSource(params string[] parts)
    {
        var file = Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
        Assert.True(File.Exists(file), $"Missing file: {file}");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(File.ReadAllBytes(file));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln"))
                && Directory.Exists(Path.Combine(dir.FullName, "TodoX.Web")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate todoX-Dashboard-SaaS repo root.");
    }
}
