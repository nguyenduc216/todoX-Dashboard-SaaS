namespace TodoX.Web.Services.AiProviders;

public sealed record ProviderCredentialKey(int KeyVersion, byte[] KeyMaterial, string Algorithm);

public sealed record ProtectedProviderCredential(
    byte[] Ciphertext,
    byte[] Nonce,
    byte[] AuthTag,
    int KeyVersion,
    string EncryptionAlgorithm,
    string Fingerprint,
    string? MaskedHint);

public sealed class ProviderAccountCredentialMetadata
{
    public Guid ProviderAccountId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string CredentialRole { get; set; } = string.Empty;
    public bool AccountEnabled { get; set; }
    public bool AccountDefault { get; set; }
    public bool MappingEnabled { get; set; }
    public Guid? SecureCredentialId { get; set; }
    public string? TokenFingerprint { get; set; }
    public string? MaskedHint { get; set; }
    public string? SecureStatus { get; set; }
    public string? EncryptionAlgorithm { get; set; }
    public int? KeyVersion { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class ProviderCredentialAccount
{
    public Guid Id { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
    public int Priority { get; set; } = 100;
    public string? ConfigJson { get; set; }
}

public sealed class ProviderCredentialMapping
{
    public Guid Id { get; set; }
    public Guid ProviderAccountId { get; set; }
    public Guid? SecureCredentialId { get; set; }
    public string CredentialRole { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Priority { get; set; } = 100;
}

public sealed class ProviderSecureCredentialRecord
{
    public Guid Id { get; set; }
    public Guid ProviderAccountId { get; set; }
    public string CredentialRole { get; set; } = string.Empty;
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
    public byte[] Nonce { get; set; } = Array.Empty<byte>();
    public byte[] AuthTag { get; set; } = Array.Empty<byte>();
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public int KeyVersion { get; set; }
    public string TokenFingerprint { get; set; } = string.Empty;
    public string? MaskedHint { get; set; }
    public string Status { get; set; } = "active";
    public DateTime ValidFrom { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class ResolvedProviderCredential
{
    public Guid ProviderAccountId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string CredentialRole { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string? MaskedHint { get; init; }

    public override string ToString()
        => $"ResolvedProviderCredential ProviderCode={ProviderCode} CredentialRole={CredentialRole} Secret=***";
}

public sealed class Ai79CredentialMigrationResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ProviderAccountCredentialMetadata? Metadata { get; set; }

    public override string ToString()
        => $"Ai79CredentialMigrationResult Status={Status} Success={Success}";
}
