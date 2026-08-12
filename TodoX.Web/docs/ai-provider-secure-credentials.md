# AI Provider Secure Credentials

TodoX provider credentials now resolve through provider account credential mappings and `system.ai_provider_credentials_secure`.

The master encryption key is stored in `system.ai_provider_credential_master_key` so a `todo_saas` backup/restore remains operational without IIS environment variables, DPAPI, server-local certificates, or manual per-server secret setup.

This is an intentional operational trade-off:

- It improves portability and avoids accidental plaintext credential exposure.
- It allows encrypted credentials to survive normal database restore workflows.
- It is weaker than external KMS/HSM separation if an attacker obtains complete database access.
- The key store is behind `IProviderCredentialKeyStore`, so Azure Key Vault, KMS, or HSM can replace it later without changing provider consumers.

Never expose key material, ciphertext, nonce, authentication tag, or plaintext secrets to UI, logs, reports, sync history, provider config, or test snapshots.
