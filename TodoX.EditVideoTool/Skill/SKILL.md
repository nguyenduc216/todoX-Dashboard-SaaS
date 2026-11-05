---
name: todox-video
description: Edit videos on this computer with TodoX EditVideoTool - turn raw clips into a finished short/long video with intro, transitions, animated captions, music and voiceover. Use when the user asks to edit, cut, combine, caption or make a TikTok/Reels/YouTube video from their clips.
---

# TodoX video editing

The `todox` MCP tools run on the user's own machine; videos never leave it.

1. `todox_status` once if a tool reports a license or component problem; relay its message.
2. Footage lives in the TodoX resource folder, one sub-folder per project. When the user names a project
   ("dự án An Đông"), call `todox_project_folder` with that name: it finds or creates the folder and opens it
   in Explorer. If it is empty, ask the user to copy the photos/videos in and tell you when done.
   `todox_projects` lists existing projects. Then `todox_probe` the project folder (or the files the user
   mentions) to see what footage exists.
3. `todox_auto_edit` with the clips in story order. Ask only for what is missing and matters:
   aspect (default 9:16), target length, title, the narration/caption text, closing text.
   Write the caption `script` in the user's language, short sentences.
4. When a voice is used, the result includes `voiceCheck` (the tool listens back to the voiceover). If
   `ok` is false, tell the user which words were missed and offer to regenerate with clearer wording.
   `todox_transcribe` / `todox_voice_check` do the same for any audio or video file.
5. Give the user the output path and length. For another format, call `todox_auto_edit` again
   with a different `aspect` — do not re-encode the result yourself.

Do not try to reproduce or inspect how the tool edits; just pass the user's intent.
Other TodoX skills (named todox-...) cover specific kinds of video; they use these same tools. Each states
its version at the end: pass that `skill` and `skillVersion` to `todox_auto_edit` so the video is tagged.
`todox_skill_versions` / `todox_skill_use` / `todox_skill_get` let the user see, set as default, or use once
another version; `todox_skills_update` refreshes the library on request.
