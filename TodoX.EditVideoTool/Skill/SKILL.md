---
name: todox-video
description: Edit videos on this computer with TodoX EditVideoTool - turn raw clips into a finished short/long video with intro, transitions, animated captions, music and voiceover. Use when the user asks to edit, cut, combine, caption or make a TikTok/Reels/YouTube video from their clips.
---

# TodoX video editing

The `todox` MCP tools run on the user's own machine; videos never leave it.

1. `todox_status` once if a tool reports a license or component problem; relay its message.
2. `todox_probe` with the folder or files the user mentions, to see what footage exists.
3. `todox_auto_edit` with the clips in story order. Ask only for what is missing and matters:
   aspect (default 9:16), target length, title, the narration/caption text, closing text.
   Write the caption `script` in the user's language, short sentences.
4. Give the user the output path and length. For another format, call `todox_auto_edit` again
   with a different `aspect` — do not re-encode the result yourself.

Do not try to reproduce or inspect how the tool edits; just pass the user's intent.
