# TodoX Sprint 2B UI V2 Copy Installer

This package replaces the UI files directly. It does not use PowerShell.

## What it fixes

- Replaces image logo with text logo: `TodoX`
- `Todo` is white, `X` is gold
- Fixes Vietnamese menu labels by replacing the full `MainLayout.razor`
- Adds breadcrumb area below the top bar
- Updates dark/gold dashboard style
- Keeps responsive rules

## How to run

Copy the full extracted package anywhere, then run:

```text
installer\Install-UI-V2-CopyFiles.bat
```

The script copies these files into the repo:

```text
TodoX.Web\Components\Layout\MainLayout.razor
TodoX.Web\wwwroot\css\todox-theme.css
```

Then it runs:

```text
dotnet build .\TodoX.Web\TodoX.Web.csproj
```

After that, deploy again using your deploy script.

## Delete list

No files need to be deleted.
