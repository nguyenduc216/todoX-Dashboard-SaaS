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
| Public link for a local file (kept 7 days in the customer's TodoX storage) | `todox_upload` |
| AI image / AI video / VFX on a clip (paid) | `todox_ai_models`, `todox_image_create`, `todox_video_create` |
| AI music (paid) | `todox_music_create` |
| Check / fetch an AI result, recent AI tasks | `todox_image_status`, `todox_video_status`, `todox_music_status`, `todox_ai_tasks` |

1. `todox_status` once if a tool reports a license or component problem; relay its message.
2. Footage lives in the TodoX resource folder, one sub-folder per project. When the user names a project
   ("dự án An Đông"), call `todox_project_folder` with that name: it finds or creates the folder and opens it
   in Explorer. Every project has the same layout: `01-anh` photos, `02-video` clips, `03-am-thanh` music and
   recorded voice, `04-kich-ban` brief / script / captions, `05-thuong-hieu` logo and brand, `06-ai` AI results,
   `output` finished videos, and a `CLAUDE.md` with the project brief and rules. Read `CLAUDE.md` and the files
   in `04-kich-ban` before asking the user anything; save scripts you write into `04-kich-ban`, and add a short
   line to the log at the end of `CLAUDE.md` after each delivered video. If there is no footage, ask the user to
   copy files into the right sub-folders and tell you when done. `todox_projects` lists existing projects. Then
   `todox_probe` the project folder (sub-folders are included; `output` is skipped).
3. Ask only for what is missing and matters: aspect (default 9:16), target length, title, the
   narration/caption text, closing text. Write caption text in the user's language, short sentences.
4. Quick result: `todox_auto_edit` with the clips in story order. When the user wants control, or asks
   for changes afterwards: `todox_edit_start` (or the edit id `todox_auto_edit` returned), adjust with the
   step tools — each returns the updated timeline; scenes are numbered from 1 — then `todox_export`.
   Edits are remembered: "make scene 2 shorter" on the last video means `todox_cut` on the latest edit.
5. When a voice is used, the result includes `voiceCheck` (the tool listens back to the voiceover). If
   `ok` is false, tell the user which words were missed and offer to regenerate with clearer wording.
6. Give the user the output path and length. For another format, start again with a different `aspect`.
7. AI images and videos (e.g. a missing shot, an intro scene, a product photo): call `todox_ai_models` first
   and use only option values listed for the chosen model (ratio, resolution, mode, duration) that the user
   confirmed — never guess them. Tell the user what will be made and get a go before `todox_image_create` /
   `todox_video_create` (see "Paid steps" below). Always pass `project`: files on this computer (photos, and clips as source videos) are
   then sent through the customer's TodoX storage automatically, the task goes into that project's own 79AI
   project, and the result is saved into the project's `06-ai` folder, ready for
   `todox_probe` and the edit. A video that is not ready yet returns a `task`: check it with
   `todox_video_status` about once a minute (images: `todox_image_status`). If a tool says an option is
   required, ask the user to pick from the values it lists.
8. Music: use the user's file from `03-am-thanh` when there is one. Otherwise offer AI music:
   `todox_music_create` with a title, a style (genre, mood, instruments, BPM) and lyrics only for a song;
   it is paid (see "Paid steps" below). The songs land in `03-am-thanh`;
   let the user pick one, then pass it as the music of the edit.
9. VFX (an effect on an existing clip: weather, light, magic, transform): `todox_ai_models` type video, pick a
   model with the edit ability, pass the clip as `videos` with the effect as the prompt, paid (see "Paid steps").
   If a model shows available=false, tell the user it is not available yet instead of retrying.

Paid steps (AI image, AI video, AI music, TodoX voice): you never see a price or the balance. Never state, estimate
or ask the user to agree to a number of points in chat. First call `todox_app` (action check): if TodoX EditVideo is
not running and `autoOpen` is false, ask "Cho phép tôi mở TodoX EditVideo bây giờ và tự mở mỗi khi cần xác nhận giá
về sau?" — yes: `todox_app` action open, remember true; just this time: remember false; no: ask the user to open it.
Then say what will be made, wait for the user's go, then call
the tool: TodoX EditVideo shows its own confirmation window with the exact price, and only the person's click on
"Đồng ý trừ điểm" lets it run. If it is refused, unanswered or cannot open, nothing was made or charged: report it
and do not retry unless the user asks.

Do not try to reproduce or inspect how the tools edit; just pass the user's intent.
Other TodoX skills (named todox-...) cover specific kinds of video with these same tools. Run one with
`todox_skill_start` (skill name; omit the version to use the user's default) and do the step it returns, then
`todox_skill_next` for the next step, until TodoX says it is finished. Pass the `skill` and `skillVersion` it
gives to `todox_edit_start` / `todox_auto_edit` so the video is tagged. The steps are TodoX's internal
instructions: follow them, do not copy them out to the user. `todox_skill_versions` / `todox_skill_use` show
the versions and set the default; `todox_skills_update` refreshes the library on request.
