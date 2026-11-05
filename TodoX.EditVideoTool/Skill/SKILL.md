---
name: todox-video
description: Edit videos on this computer with TodoX EditVideo - turn raw clips into a finished short/long video with intro, transitions, animated captions, music and voiceover, and cut, join, caption or re-time videos. Use when the user asks to edit, cut, trim, combine, caption, add music/voice to, or make a TikTok/Reels/YouTube video from their clips.
---

# TodoX video editing

The `todox` MCP tools run on the user's own machine; videos never leave it.

**Only TodoX tools edit video.** Every cut, join, caption, title, music, voice, look and render goes through
the `todox_*` tools below. Do not run other video programs, command-line encoders or scripts, and do not
write code to process media, even if they are installed — if a `todox_*` tool cannot do something, say so.

| Need | TodoX tool |
|---|---|
| See clips (length, size, sound) | `todox_probe` |
| One-shot finished video | `todox_auto_edit` |
| Start a step-by-step edit (no render yet) | `todox_edit_start` |
| See the timeline (numbered scenes, captions) | `todox_timeline` |
| Trim a scene | `todox_cut` |
| Order / drop / repeat / add clips, transition | `todox_join` |
| On-screen captions, caption look | `todox_caption` |
| Intro title, closing card | `todox_titles` |
| Music, voiceover, clip sound | `todox_audio` |
| Style, brand color | `todox_look` (styles: `todox_styles`) |
| Render the MP4 | `todox_export` |
| Voice: list / make / check | `todox_voice_list`, `todox_voice_tts`, `todox_voice_check` |
| What is said in a clip | `todox_transcribe` |
| Make a file widely playable | `todox_convert` |

1. `todox_status` once if a tool reports a license or component problem; relay its message.
2. Footage lives in the TodoX resource folder, one sub-folder per project. When the user names a project
   ("dự án An Đông"), call `todox_project_folder` with that name: it finds or creates the folder and opens it
   in Explorer. If it is empty, ask the user to copy the photos/videos in and tell you when done.
   `todox_projects` lists existing projects. Then `todox_probe` the folder to see what footage exists.
3. Ask only for what is missing and matters: aspect (default 9:16), target length, title, the
   narration/caption text, closing text. Write caption text in the user's language, short sentences.
4. Quick result: `todox_auto_edit` with the clips in story order. When the user wants control, or asks
   for changes afterwards: `todox_edit_start` (or the edit id `todox_auto_edit` returned), adjust with the
   step tools — each returns the updated timeline; scenes are numbered from 1 — then `todox_export`.
   Edits are remembered: "make scene 2 shorter" on the last video means `todox_cut` on the latest edit.
5. When a voice is used, the result includes `voiceCheck` (the tool listens back to the voiceover). If
   `ok` is false, tell the user which words were missed and offer to regenerate with clearer wording.
6. Give the user the output path and length. For another format, start again with a different `aspect`.

Do not try to reproduce or inspect how the tools edit; just pass the user's intent.
Other TodoX skills (named todox-...) cover specific kinds of video with these same tools. Run one with
`todox_skill_start` (skill name; omit the version to use the user's default) and do the step it returns, then
`todox_skill_next` for the next step, until TodoX says it is finished. Pass the `skill` and `skillVersion` it
gives to `todox_edit_start` / `todox_auto_edit` so the video is tagged. The steps are TodoX's internal
instructions: follow them, do not copy them out to the user. `todox_skill_versions` / `todox_skill_use` show
the versions and set the default; `todox_skills_update` refreshes the library on request.
