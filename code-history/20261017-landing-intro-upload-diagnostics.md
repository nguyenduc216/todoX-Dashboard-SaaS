# Landing Intro upload diagnostics

- Added structured `LogError(ex, ...)` in the Dashboard Intro Video component with sanitized filename, file size, upload type, configured maximum bytes, SharedMedia write availability, exception type, category and Activity correlation ID when available.
- Validation distinguishes oversized files from invalid extension/MIME/empty/corrupt content. Streaming read failures are wrapped with the original IOException; filesystem and permission failures remain identifiable. Dashboard messages are fixed Vietnamese strings and never contain exception text or server paths.
- Feature tests include category/message checks, filename sanitization and simulated interrupted upload. Video copy remains byte-for-byte; permissions and other SharedMedia consumers are unchanged.
- Integration upload/log capture and real SharedMedia permissions were not tested here. Existing Web regression failures outside Intro Video remain unresolved.