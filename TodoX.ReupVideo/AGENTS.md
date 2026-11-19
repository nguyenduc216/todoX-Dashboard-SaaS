# todoX ReupVideo (TodoX.ReupVideo)

Windows app (WinForms, .NET 10, exe `todox-reupvideo.exe`) that reuploads TikTok videos to Facebook fanpages.
Same TodoX look as TodoX EditVideo (palette, logo, thin yellow scroll bar `App/ScrollHost.cs`, dark list bars).

## Rules

- **Offline, local only.** Nothing is sent to the TodoX server. Settings, links, run history and daily logs live in
  `%LOCALAPPDATA%\TodoX\ReupVideo` (override: `TODOX_REUPVIDEO_HOME`). The app only talks to TikTok (via yt-dlp),
  Facebook Graph API and yt-dlp's GitHub release.
- **No license yet** (unlike EditVideo). Do not add one unless asked.
- Fanpage tokens are DPAPI-encrypted (`Storage/SecretProtector.cs`); never write them in plain text or logs.
- Logs are not shown in the window (only the latest step on Trang chính); they go to `logs\yyyy-MM-dd.log`.
- The window's X hides to the tray; the app exits only from the tray icon menu → "Thoát todoX ReupVideo".
- UI sizes are 96-DPI pixels passed through `Theme.S()`/`Theme.P()` (screens at 125/150%).

## Flow (`Core/ReupRunner.cs`)

Scan ticked TikTok channels (yt-dlp `--flat-playlist`) → new links into `library.json` → for up to N pending links:
check the link is still live (deleted/private = "không còn active", never posted) → download once → post to every
ticked fanpage that has not got it (Reels ≤ 90 s, else normal video) with the TikTok text + hashtags
(`Core/CaptionBuilder.cs`). Failures (check, download or post) go to the "reup lại" list: retried automatically up to
`MaxAttempts`, and by hand ("Reup lại") without limit. Every run is saved in `history.json` (`Storage/RunHistory.cs`).

Schedule (`Core/SchedulePlanner.cs`): manual, daily times, or every N minutes; runs only while the app is open
(optionally started with Windows, minimized to the tray).

## Build / test

```
dotnet build TodoX.ReupVideo/TodoX.ReupVideo.csproj
dotnet test TodoX.ReupVideo.Tests/TodoX.ReupVideo.Tests.csproj
dotnet publish TodoX.ReupVideo/TodoX.ReupVideo.csproj -c Release   # single self-contained exe
```
