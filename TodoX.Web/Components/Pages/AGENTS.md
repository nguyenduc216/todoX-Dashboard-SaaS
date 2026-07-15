# Page Editing Guardrails

- Only edit pages explicitly named by the user for the current task.
- Do not rewrite an entire Razor file when a small patch is enough.
- Do not run bulk encoding conversion scripts across the repository.
- Do not use default PowerShell `Get-Content` / `Set-Content` to write files that contain Vietnamese text.
- Keep `.razor`, `.cs`, `.json`, and `.md` files as UTF-8.
- After editing, scan changed files for mojibake and replacement characters.
- Do not change Vietnamese strings outside the requested scope.
- Before completion, report every page changed.
- Do not edit other pages to clean code unless the user explicitly asks.
- Do not create or run migrations.
