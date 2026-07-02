namespace TodoX.Web.Services;

/// <summary>BCrypt password hashing to match todo_saas Foundation V2 ($2a$ hashes).</summary>
public sealed class PasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);

    public bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        // Guard against placeholder / non-bcrypt values (valid bcrypt hashes are 60 chars).
        if (storedHash.Length < 60 || !storedHash.StartsWith("$2"))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, storedHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <summary>True when a stored hash is missing/placeholder and should be repaired.</summary>
    public static bool IsPlaceholder(string? storedHash)
        => string.IsNullOrWhiteSpace(storedHash) || storedHash.Length < 60 || !storedHash.StartsWith("$2");
}
