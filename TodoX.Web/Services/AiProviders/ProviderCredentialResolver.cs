namespace TodoX.Web.Services.AiProviders;

public interface IProviderCredentialResolver
{
    Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default);
}

public sealed class ProviderCredentialResolver : IProviderCredentialResolver
{
    private readonly IProviderCredentialRepository _repository;
    private readonly IProviderCredentialProtector _protector;

    public ProviderCredentialResolver(IProviderCredentialRepository repository, IProviderCredentialProtector protector)
    {
        _repository = repository;
        _protector = protector;
    }

    public async Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
    {
        var normalizedProviderCode = string.IsNullOrWhiteSpace(providerCode)
            ? throw new InvalidOperationException("Provider credential is not configured.")
            : providerCode.Trim().ToLowerInvariant();
        var normalizedRole = string.IsNullOrWhiteSpace(credentialRole)
            ? throw new InvalidOperationException("Provider credential is not configured.")
            : credentialRole.Trim();

        var account = await _repository.GetPreferredAccountAsync(normalizedProviderCode, "production", ct)
            ?? throw new InvalidOperationException("Provider credential is not configured.");
        var mapping = await _repository.GetActiveMappingAsync(account.Id, normalizedRole, ct)
            ?? throw new InvalidOperationException("Provider credential is not configured.");
        var secureCredentialId = mapping.SecureCredentialId
            ?? throw new InvalidOperationException("Provider credential is not configured.");
        var secure = await _repository.GetSecureCredentialAsync(secureCredentialId, ct)
            ?? throw new InvalidOperationException("Provider credential is not configured.");

        if (!string.Equals(secure.Status, "active", StringComparison.OrdinalIgnoreCase)
            || secure.ValidFrom > DateTime.UtcNow
            || (secure.ExpiresAt is not null && secure.ExpiresAt <= DateTime.UtcNow))
        {
            throw new InvalidOperationException("Provider credential is not active.");
        }

        var secret = await _protector.UnprotectAsync(secure, ct);
        await _repository.UpdateLastUsedAsync(secure.Id, ct);

        return new ResolvedProviderCredential
        {
            ProviderAccountId = account.Id,
            ProviderCode = account.ProviderCode,
            CredentialRole = normalizedRole,
            Secret = secret,
            MaskedHint = secure.MaskedHint
        };
    }
}
