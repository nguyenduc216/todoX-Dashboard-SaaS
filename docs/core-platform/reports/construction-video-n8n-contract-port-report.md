# Construction Timelapse n8n Contract Port Report

Date: 2026-08-16

## Source Of Truth

Audited released Construction Timelapse workflow folder:

`D:\todoX\workflow\Released\contruction-timeslapse`

Workflow files audited:

- `todox_timelapse_02_input_profile_mode_v5.json`
- `todox_timelapse_03_image_worker_v5_hierarchical_reverse.json`
- `todox_timelapse_05_video_submit_v4.4_worker_anchor_prompt.json`
- `todox_timelapse_05P_poller_v5.json`
- `todox_timelapse_06_finalize_v6.9_legacy_es5_compatible.json`
- `todox_timelapse_07_retry_v5.5_grouped_layout.json`

This report intentionally does not use the RVideo worker as source of truth.

## Exact Node Names

- Original image upload: `79AI - Upload Original`
- Generated image submit: `79AI - Create Progress Frame`
- Generated image parser: `Parse Create Image`
- Start/end input and prompt builder: `Route Clip`
- Seedance video submit: `79AI - Create Video Clip`
- Video submit response parser: `Inspect 79AI Create Response`
- Video poll: `79AI - Check Video Once`
- Video poll fallback: `79AI - List Videos Fallback`
- Poll classifier: `Classify Poll Result`
- Retry handler: `DB - Prepare Video Retry`, `Trigger Video Submit Retry`, `Trigger Video Poller`
- Finalizer: `DB - Claim FINALIZING`, `Prepare FFmpeg Merge`, `Run FFmpeg Merge Direct`, `DB - Mark COMPLETED`

## Submit Contract

Generated progress image submit:

`POST /generateImage`

Fields: `access_token`, `domain`, `action_type=create`, `model`, `prompt`, `editImage=true`, `base64Image`, `project_id=default`, `subjects=[]`, `ratio`, `resolution=1k`, `mode=vip`.

Seedance video submit:

`POST /create-video`

Fields: `access_token`, `domain`, `model`, `privacy=PRIVATE`, `prompt`, `translate_to_en=false`, `project_id=default`, `ratio`, `resolution`, `duration`, `mode`, `images`.

`images` is an images JSON descriptor array in deterministic order: start image, then end image. Each descriptor contains exactly `id_base`, `project_id`, `url`, and `file_name`. The Timelapse video path does not use `image` or `image_2`.

## Image Handling

Original/customer image handling comes from node `79AI - Upload Original`:

`POST /image-upload`

Fields: `access_token`, `domain`, `data` as raw base64 without a data URI prefix, `project_id=default`, `file_name`, and `size`.

The upload response must contain `imageInfo.id_base` and `imageInfo.url`. C# now uploads/registers a TodoX media image with 79AI when the persisted image stage response does not already contain `imageInfo.id_base`. It does not fake provider ids and does not use TodoX media ids as 79AI ids.

Generated image handling comes from `Parse Create Image`: C# uses the persisted 79AI image response `imageInfo.id_base` and URL when present.

All URLs sent to 79AI video submit are validated as absolute HTTP(S) URLs and are resolved from configured public URL settings when TodoX stores a relative upload URL.

## Poll Contract

Primary poll:

`POST /video`

Fields: `access_token`, `domain`, `videoId`.

Fallback poll:

`POST /videos`

Fields: `access_token`, `domain`. C# only uses the fallback row that matches the existing `provider_task_id`/`videoId`.

Image poll remains `POST /image` with `id_base`.

## Status And Output URL

Success statuses: `MEDIA_GENERATION_STATUS_SUCCESSFUL`, `MEDIA_GENERATION_COMPLETED`, `SUCCESS`, `SUCCEEDED`, `COMPLETED`, `COMPLETE`, `DONE`.

Failure statuses: `MEDIA_GENERATION_STATUS_FAILED`, `MEDIA_GENERATION_FAILED`, `FAILURE`, `FAILED`, `ERROR`, `REJECTED`, `CANCELLED`, `CANCELED`.

Other pending/unknown statuses remain `RUNNING`.

Video output URL is read only from video-specific containers and fields: `download_url`, `downloadUrl`, `video_url`, `videoUrl`, `source_url`, `sourceUrl`, `file_url`, `fileUrl`, `output_url`, `outputUrl`, and `url`.

Equivalent `data.videoInfo`, `body.videoInfo`, and `body.data.videoInfo` shapes are supported. C# does not recursively scan arbitrary response URLs for video output, avoiding accidental input image URL pickup.

## Continuity Prompt

Prompt semantics were ported from `Route Clip`: start/end progress, profile JSON semantics, endpoint anchor rule, worker rule, and strict chain rule.

C# now enforces `@image1` as the exact opening anchor, `@image2` as the exact closing anchor, same building/architecture/footprint/floor count/windows/openings/roof geometry/camera/lens/perspective/framing/environment, no demolition/reset/rebuild/disappearing/reappearing structure/duplicate build/scene cut/architecture morph, never remove permanent elements visible in `@image1`, only advance work necessary to reach `@image2`, and final convergence to `@image2`.

`Route Clip` uses these profile fields for the video prompt:

- `phase_rules[].phase_goal`
- `phase_rules[].prompt_fragment`
- `phase_rules[].worker_actions`
- `phase_rules[].must_exist`
- `phase_rules[].must_not_exist`
- fallback `scene_templates[].phase_goal`
- fallback `scene_templates[].prompt_fragment`
- `continuity_rules.must_preserve`
- `continuity_rules.must_avoid`
- `video_generation.video_clip_prompt_template`
- `profile_name` / `prompt_profile_code`

It does not serialize the full profile JSON object into the provider prompt. C# now follows that behavior and ignores metadata fields such as ids, enabled flags, categories, select order, timestamps, and other database/configuration fields not used by the model.

Provider prompt budget:

- maximum sent to 79AI: 4200 characters
- mandatory anchor/strict continuity rules are always preserved
- optional profile-derived text is fit into the remaining budget
- `request_json` includes `prompt_length`, `profile_prompt_length`, and `profile_prompt_truncated`

This fixes the live failure where the previous resolver prepended the entire profile JSON object, pushing clip 4 prompt length beyond the 79AI 5000-character limit.

## Business Rules

The Construction Timelapse scene mappings remain:

- 3: `0,35,70,100`
- 4: `0,25,50,75,100`
- 5: `0,20,40,60,80,100`
- 6: `0,25,40,55,70,75,90,100`

In current code, `SceneCount` means the selected TodoX product preset. For the 6-scene preset, the authoritative product mapping contains eight checkpoints and therefore seven video clips.

## C# Changes

- Added 79AI `/image-upload` client method for original image registration.
- Added video poll fallback to `/videos` with exact provider task id matching.
- Added `DefaultImageUploadPath=/image-upload`.
- Aligned default Timelapse image resolution with n8n `resolution=1k`.
- Updated video submit descriptor building to upload TodoX media when no persisted `imageInfo.id_base` exists.
- Added prompt snapshot fields to video work-item claim so video prompt can use the Timelapse profile data.
- Strengthened server-side video prompt continuity rules.

## Cancellation And Transient Handling

Existing worker behavior is preserved: cancellation and transient HTTP 408, 429, 500, 502, 503, and 504 poll failures release the claim, keep `provider_task_id`, and do not overwrite useful `response_json` with `{}`.

## Tests And Build

Regression coverage added/updated for original image `/image-upload`, generated image id_base, video `images` descriptor order, `videoId` poll, `/videos` fallback, success/failure statuses, output URL parsing, strict continuity prompt, 6-scene mapping, and Construction Timelapse workflow source guard.

Validation commands and final commit SHA are recorded in the task response.

## Remaining Risks

- Real 79AI smoke testing is still required to verify live provider acceptance.
- Existing production failed task recovery depends on retry/resume leaving `provider_task_id` intact for clips that already submitted successfully.

Ready for smoke test after deploy: YES.
