# RDANCE SUCCESS / FAILED Provider Comparison

Audit date: 2026-09-23

Scope: read-only forensic audit for RDance / `dance_sell`, provider `79ai`, model `kling_video_motion_3`.

Constraints honored:

- No production code changed.
- No database writes.
- No migration.
- No provider calls.
- No retry/upload/submit/poll.
- No paid task.
- No commit/push/deploy.

## 1. Evidence Status

The three task groups requested are:

| Label | DanceSellJob | RenderJob | ProviderTask |
|---|---|---|---|
| FAILED NEW | `23805c3f-1d44-465f-be30-cde61aafdbfe` | `eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b` | `58660402320274b2` |
| FAILED PREVIOUS | `28171031-d86d-4c67-8eff-67aab154df5f` | `3fe09576-48b5-4e02-ba00-c89ad4a0572f` | `56a3fb0578396662` |
| SUCCESS | `49554a92-900a-42ec-8f81-543faa0bfe95` | `c95e0823-32e7-4e27-8752-9ac9bf73a607` | `842d3440df5430b8` |

Local workspace evidence contains source code and older forensic reports, but it does **not** contain runtime rows for all three requested Dashboard tasks. Therefore the exact per-task table below deliberately marks missing runtime values as `UNKNOWN` instead of inventing them.

Previous local reports confirmed:

- `56a3fb0578396662` reached provider task creation and later provider `ERROR`.
- `842d3440df5430b8` is a task-supplied success ID.
- The three requested target rows were not present in the available local runtime export.

## 2. Source-Confirmed Wire Contract

The effective Dashboard RDance submit path is:

`DanceSellRenderHandler.Submit79AiAsync` -> `Ai79TaskClient.SubmitMotionControlAsync`.

For the URL-based path currently used by Dashboard RDance, source confirms:

```text
POST {baseUrl}{motionSubmitPath}
Authorization: Bearer <redacted>
Content-Type: application/x-www-form-urlencoded

domain=<domain>
project_id=<project_id>
model=<model>
prompt=<prompt>
image_url=<reference provider URL>
video_url=<motion provider URL>
subType=<subType>
background_source=<background_source>
mode=<mode>
ratio=<ratio>
images[0][url]=<reference provider URL> when IncludeImagesZeroUrl=true
```

`request_json` is an internal diagnostic snapshot, not the serialized HTTP body.

Source-confirmed defaults/resolution:

| Field | Value / Resolution | Evidence |
|---|---|---|
| provider | `79ai` | source / route |
| model | `kling_video_motion_3` | task + source |
| submit endpoint | `/ai/jobs/video/kling_video_motion_3` unless route overrides | source |
| base URL | route/account/provider config, fallback `https://api.gommo.net/ai` | source |
| domain | account/provider config, fallback `79ai.net` | source |
| project_id | route config, fallback `default` | source |
| subType | route config, fallback `motion` | source |
| background_source | route config, fallback `input_video` | source |
| mode | resolved from route/job mode | source |
| ratio | resolved from route/job ratio | source |
| images[0][url] | included unless route config `include_images_zero_url=false` | source |

## 3. Dashboard Comparison Table

Legend:

- `CONFIRMED FROM SOURCE`: source code confirms construction or default behavior.
- `TASK-SUPPLIED`: value came from the user-provided task facts.
- `UNKNOWN`: exact runtime DB/provider row is not available locally.
- `INFERRED`: derived from source/config, not captured wire traffic.

| Field | FAILED `586604...` | FAILED `56a3...` | SUCCESS `842d...` |
|---|---|---|---|
| DanceSellJob | `23805c3f...` TASK-SUPPLIED | `28171031...` TASK-SUPPLIED | `49554a92...` TASK-SUPPLIED |
| RenderJob | `eefdf7e4...` TASK-SUPPLIED | `3fe09576...` TASK-SUPPLIED | `c95e0823...` TASK-SUPPLIED |
| ProviderTask | `58660402320274b2` TASK-SUPPLIED | `56a3fb0578396662` TASK-SUPPLIED | `842d3440df5430b8` TASK-SUPPLIED |
| reference_mode | UNKNOWN | UNKNOWN | UNKNOWN |
| placement | UNKNOWN | UNKNOWN | UNKNOWN |
| mode | UNKNOWN runtime; provider submit mode resolved by source | UNKNOWN runtime; provider submit mode resolved by source | UNKNOWN runtime; provider submit mode resolved by source |
| orientation | UNKNOWN | UNKNOWN | UNKNOWN |
| character_orientation | UNKNOWN | UNKNOWN | UNKNOWN |
| business prompt | UNKNOWN | UNKNOWN | UNKNOWN |
| video_prompt | UNKNOWN | UNKNOWN | UNKNOWN |
| image_prompt | UNKNOWN | UNKNOWN | UNKNOWN |
| reference media_id | UNKNOWN | previously reported local media `1cdb72e1...` in older audit, not current row export | previously reported local reference filename `c5c2fcb...png`, not current row export |
| reference object_key | UNKNOWN | UNKNOWN | UNKNOWN |
| reference mime_type | UNKNOWN | likely PNG from prior report, not target row export | likely PNG from prior report, not target row export |
| reference bytes | UNKNOWN | UNKNOWN | UNKNOWN |
| reference provider idBase | UNKNOWN | prior audit mentions known provider reference `19c06fda7204a83d.png`; exact row unavailable | prior audit mentions provider reference `20488258f158151a.png`; exact row unavailable |
| reference upload URL | UNKNOWN | UNKNOWN | UNKNOWN |
| reference verified URL | UNKNOWN | UNKNOWN | UNKNOWN |
| reference verification status | upload/list verification succeeded TASK-SUPPLIED | upload/list verification succeeded TASK-SUPPLIED | upload/list verification succeeded TASK-SUPPLIED |
| reference upload/reuse state | UNKNOWN | UNKNOWN | UNKNOWN |
| motion media_id | UNKNOWN | prior audit mentions local motion filename, not row export | prior audit mentions local motion filename, not row export |
| motion object_key | UNKNOWN | UNKNOWN | UNKNOWN |
| motion source URL | UNKNOWN | UNKNOWN | UNKNOWN |
| motion mime_type | UNKNOWN | likely `video/mp4` from prior report, not target row export | likely `video/mp4` from prior report, not target row export |
| motion bytes | UNKNOWN | UNKNOWN | UNKNOWN |
| motion provider idBase | UNKNOWN | UNKNOWN | UNKNOWN |
| motion upload URL | UNKNOWN | UNKNOWN | UNKNOWN |
| motion verified URL | UNKNOWN | UNKNOWN | UNKNOWN |
| motion download_url | UNKNOWN | UNKNOWN | UNKNOWN |
| motion verification status | upload/list verification succeeded TASK-SUPPLIED | upload/list verification succeeded TASK-SUPPLIED | upload/list verification succeeded TASK-SUPPLIED |
| provider account id | UNKNOWN | UNKNOWN | UNKNOWN |
| provider code | `79ai` TASK-SUPPLIED / SOURCE | `79ai` TASK-SUPPLIED / SOURCE | `79ai` TASK-SUPPLIED / SOURCE |
| base URL | UNKNOWN runtime; fallback/source path known | UNKNOWN runtime; fallback/source path known | UNKNOWN runtime; fallback/source path known |
| domain | UNKNOWN runtime; source fallback `79ai.net` | UNKNOWN runtime; source fallback `79ai.net` | UNKNOWN runtime; source fallback `79ai.net` |
| project_id | UNKNOWN runtime; source fallback `default` | UNKNOWN runtime; source fallback `default` | UNKNOWN runtime; source fallback `default` |
| model | `kling_video_motion_3` TASK-SUPPLIED | `kling_video_motion_3` TASK-SUPPLIED | `kling_video_motion_3` TASK-SUPPLIED |
| subType | UNKNOWN runtime; source fallback `motion` | UNKNOWN runtime; source fallback `motion` | UNKNOWN runtime; source fallback `motion` |
| background_source | UNKNOWN runtime; source fallback `input_video` | UNKNOWN runtime; source fallback `input_video` | UNKNOWN runtime; source fallback `input_video` |
| submit mode | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime |
| ratio | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime |
| IncludeImagesZeroUrl | UNKNOWN runtime; source default true | UNKNOWN runtime; source default true | UNKNOWN runtime; source default true |
| submit prompt | UNKNOWN exact. Source uses `video_prompt`, then `prompt` fallback. | UNKNOWN exact. Source uses `video_prompt`, then `prompt` fallback. | UNKNOWN exact. Source uses `video_prompt`, then `prompt` fallback. |
| image_url | UNKNOWN | UNKNOWN | UNKNOWN |
| video_url | UNKNOWN | UNKNOWN | UNKNOWN |
| images[0][url] | UNKNOWN actual; source includes when enabled | UNKNOWN actual; source includes when enabled | UNKNOWN actual; source includes when enabled |
| submit timestamp | UNKNOWN | UNKNOWN | UNKNOWN |
| submit elapsed | UNKNOWN | UNKNOWN | UNKNOWN |
| submit response | submit succeeded, task created TASK-SUPPLIED | submit succeeded, task created TASK-SUPPLIED | submit succeeded, task created TASK-SUPPLIED |
| first poll status | RUNNING TASK-SUPPLIED | RUNNING TASK-SUPPLIED from prior audit | UNKNOWN |
| terminal status | ERROR TASK-SUPPLIED | ERROR TASK-SUPPLIED from prior audit | SUCCESS TASK-SUPPLIED |
| terminal raw response | provider returned `status=ERROR`, empty result URLs, null created_at TASK-SUPPLIED | UNKNOWN exact JSON | UNKNOWN exact JSON |
| task created -> terminal | UNKNOWN | UNKNOWN | UNKNOWN |

## 4. Reconstructed Effective HTTP Form Fields

This is a reconstruction from source and runtime configuration rules, not captured wire traffic.

| Form field | FAILED `586604...` | FAILED `56a3...` | SUCCESS `842d...` | Evidence level |
|---|---|---|---|---|
| domain | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime | source says account/provider fallback `79ai.net` |
| project_id | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime | source says route fallback `default` |
| model | `kling_video_motion_3` | `kling_video_motion_3` | `kling_video_motion_3` | TASK-SUPPLIED |
| prompt | UNKNOWN | UNKNOWN | UNKNOWN | needs operation request_json / event / row |
| image_url | UNKNOWN | UNKNOWN | UNKNOWN | needs operation asset row |
| video_url | UNKNOWN | UNKNOWN | UNKNOWN | needs operation asset row |
| subType | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime | source fallback `motion` |
| background_source | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime | source fallback `input_video` |
| mode | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime | resolved from route/job |
| ratio | UNKNOWN runtime | UNKNOWN runtime | UNKNOWN runtime | resolved from route/job |
| images[0][url] | UNKNOWN actual | UNKNOWN actual | UNKNOWN actual | source includes if enabled |

## 5. Provider Account / Project Check

Cannot confirm from local evidence whether all three tasks used the exact same provider account, credential configuration, domain, project_id, base URL, submit endpoint, and route config.

Source says they would resolve through:

```text
DanceSellRenderHandler.Resolve79AiRuntimeAsync
  -> route = provider route for MotionVideo
  -> provider by route.ProviderCode
  -> credentials.ResolveAsync(route.ProviderCode, "access_token")
  -> provider account by credential.ProviderAccountId
  -> route/account/provider config values
```

Runtime confirmation requires `dance_sell_provider_operations.provider_account_id`, render job `provider_account_id`, route config snapshot/request JSON, and provider account config for each task.

## 6. Prompt Difference

Source now uses:

```text
DanceSellMotionPromptResolver.Resolve(danceJob.VideoPrompt, danceJob.Prompt)
```

So the effective submit prompt is:

1. `video_prompt` if non-empty;
2. otherwise `prompt` if non-empty;
3. otherwise empty.

Prior forensic reports mention historical Dashboard submit may have used `prompt=""`, while old RDance may have used `/rdance`. For the three task IDs in this request, exact prompt is **UNKNOWN** until the operation/request rows are exported.

Do not assume prompt is causal. Dashboard has at least task-supplied successful jobs on the same model path.

## 7. `images[0][url]`

Source confirms:

- `image_url` is always sent in the URL-based motion submit path.
- `images[0][url]` is additionally sent when `IncludeImagesZeroUrl` is true.
- `IncludeImagesZeroUrl` defaults to true unless route config sets `include_images_zero_url=false`.
- When present, `images[0][url]` equals `image_url`.

For all three target tasks, actual presence is **UNKNOWN** without runtime route config/request snapshot. Based on source defaults, presence is **INFERRED**, not captured.

## 8. Provider Asset URL Representation

Known source behavior:

- Reference upload returns provider upload URL and idBase.
- Reference list verification can expose a URL with a different representation, including possible `?full=1`.
- Motion list verification can expose both `url` and `download_url`.
- Submit uses `referenceUrlUsed` and `motionProviderUrl` selected by Dashboard code, generally the canonical provider URL captured after upload/list verification.

For the exact tasks:

| Question | Answer |
|---|---|
| Does FAILED `586604...` submit bare `.png` or `.png?full=1`? | UNKNOWN |
| Does FAILED `56a3...` submit bare `.png` or `.png?full=1`? | UNKNOWN |
| Does SUCCESS `842d...` submit bare `.png` or `.png?full=1`? | UNKNOWN |
| Does submit use bare `.mp4`, provider list URL, or `download_url`? | UNKNOWN per task |

Previous source audit found a confirmed mechanism where stored URL vs live `?full=1` URL can reject reuse and trigger fresh upload, but that is not proven for these exact three tasks.

## 9. Timing / Media Readiness

Exact timing cannot be calculated from local evidence for the three requested tasks.

| Duration | FAILED `586604...` | FAILED `56a3...` | SUCCESS `842d...` |
|---|---|---|---|
| reference verify completed -> motion upload started | UNKNOWN | UNKNOWN | UNKNOWN |
| motion upload completed -> motion verify completed | UNKNOWN | UNKNOWN | UNKNOWN |
| motion verify completed -> submit started | UNKNOWN | UNKNOWN | UNKNOWN |
| submit started -> task created | UNKNOWN | UNKNOWN | UNKNOWN |
| task created -> first poll | UNKNOWN | UNKNOWN | UNKNOWN |
| task created -> terminal state | UNKNOWN | UNKNOWN | UNKNOWN |

Because all values are missing, there is no evidence here to support or reject a simple fixed media-readiness delay hypothesis for these exact tasks.

## 10. Old RDance Implementation

Repository search found references to the old database table:

- `public.todox_rdance_jobs`
- columns in an existing read-only collector:
  - `source_video_79ai_url`
  - `character_79ai_url`
  - `provider_request_json`
  - `provider_response_json`
  - `provider_job_id`

No n8n workflow source/export for:

- `todoX_rdance_02_prepare_source`
- `todoX_rdance_04_submit_motion`

was found in this workspace.

Therefore, old-RDance same-input comparison is **UNKNOWN** unless a read-only export from database `todoxx` / `TodoXAutomation` and n8n workflow JSON is provided.

Required export:

- old RDance row from `public.todox_rdance_jobs` matched by source video URL, TikTok URL, source media identity, or exact provider media URL;
- its `provider_request_json`;
- its `source_video_79ai_url`;
- its `character_79ai_url`;
- task ID and terminal result;
- n8n workflow JSON for the two workflows above.

## 11. Same-Input Control

Cannot identify an old RDance successful run for the same input from local evidence.

Important: matching by timestamp alone is not acceptable. The match must use actual source video URL / TikTok URL / media identity / provider media identity.

| Field | OLD RDANCE SUCCESS | DASHBOARD FAILED |
|---|---|---|
| same source input | UNKNOWN | target IDs supplied |
| reference provider asset | UNKNOWN | UNKNOWN |
| motion provider asset | UNKNOWN | UNKNOWN |
| domain | UNKNOWN | UNKNOWN |
| project_id | UNKNOWN | UNKNOWN |
| model | UNKNOWN | `kling_video_motion_3` |
| prompt | UNKNOWN | UNKNOWN |
| image_url | UNKNOWN | UNKNOWN |
| video_url | UNKNOWN | UNKNOWN |
| images[0][url] | UNKNOWN | UNKNOWN |
| subType | UNKNOWN | UNKNOWN runtime |
| background_source | UNKNOWN | UNKNOWN runtime |
| mode | UNKNOWN | UNKNOWN runtime |
| ratio | UNKNOWN | UNKNOWN runtime |
| provider account | UNKNOWN | UNKNOWN |
| upload behavior | UNKNOWN | upload/list verification succeeded TASK-SUPPLIED |
| delay before submit | UNKNOWN | UNKNOWN |
| retry behavior | UNKNOWN | UNKNOWN |
| task result | UNKNOWN | failed terminal provider status for failed Dashboard tasks |

## 12. Required Conclusions

### Q1. Do the two FAILED Dashboard tasks share a submit/config characteristic that the SUCCESS Dashboard task does not?

UNKNOWN. Source suggests the same model path and form-urlencoded contract, but exact provider account/config, prompt, URLs, and timing are unavailable.

### Q2. Does SUCCESS use a different provider account/project/domain?

UNKNOWN. Requires runtime provider account/config rows for all three operations/render jobs.

### Q3. Do FAILED tasks submit a different provider URL form?

UNKNOWN. Source sends the same field names. Exact submitted `image_url`, `video_url`, and `images[0][url]` values are unavailable.

### Q4. Is prompt different?

UNKNOWN. Source resolution is known, but exact row values for `video_prompt`/`prompt` are missing.

### Q5. Is `images[0][url]` different?

UNKNOWN actual. Source says it equals `image_url` when enabled. Whether route disabled it for any specific task is unknown.

### Q6. Is timing materially different?

UNKNOWN. No exact event timestamps for all three task groups are available locally.

### Q7. Can we identify an OLD RDance successful run for the same input?

NO from this workspace. Required old RDance DB/workflow export is missing.

### Q8. If yes, what is the first concrete divergence?

Not applicable. No same-input old RDance row identified.

### Q9. Does evidence point primarily to application request, media preparation, provider account/config, provider transient behavior, or unknown?

For these exact three tasks: **UNKNOWN**.

Based on prior related forensic reports, provider-side media/task processing or media/input-specific failure remains a plausible hypothesis, but not confirmed for the new target trio without row-level evidence.

## 13. Classification

| Proposed cause | Classification | Reason |
|---|---|---|
| Dashboard polling root cause | REJECTED | Target `586604...` reached provider `ERROR`; Dashboard normalized ERROR -> FAILED as supplied |
| Reference reuse root cause for target `586604...` | REJECTED for current target boundary | Supplied facts say reference upload/list verification succeeded; target failed after task creation |
| Different source code path/model | REJECTED | Source and task identify same `dance_sell` / `79ai` / `kling_video_motion_3` path |
| Internal `request_json` equals wire body | REJECTED | Source sends form-urlencoded body |
| Prompt difference | UNKNOWN | exact values missing |
| `images[0][url]` difference | UNKNOWN | source default known, per-task route config missing |
| Provider account/project/domain difference | UNKNOWN | runtime rows missing |
| Bare URL vs `?full=1` difference | HYPOTHESIS | source supports this mechanism, target rows missing |
| Fixed media readiness delay | HYPOTHESIS | cannot calculate target timing |
| Media/input-specific provider processing failure | HYPOTHESIS / HIGH CONFIDENCE in related prior forensics, UNKNOWN for this trio | target task `586604...` reached provider ERROR with no reason; media details missing |
| Provider transient behavior | HYPOTHESIS | provider gave ERROR with no diagnostic reason |
| Exact root cause | UNKNOWN | required runtime evidence unavailable |

## 14. Read-Only SQL Needed

Run this against the current Dashboard production database. It is `READ ONLY` and rolls back.

```sql
BEGIN TRANSACTION READ ONLY;

WITH target(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
      ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
      ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
      ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
)
SELECT 'DANCE_JOB' AS section, t.label, row_to_json(j)::text AS row_json
FROM target t
LEFT JOIN dance_sell.dance_sell_jobs j ON j.id = t.dance_sell_job_id
ORDER BY t.label;

WITH target(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
      ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
      ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
      ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
)
SELECT 'RENDER_JOB' AS section, t.label, row_to_json(r)::text AS row_json
FROM target t
LEFT JOIN render.render_jobs r ON r.id = t.render_job_id
ORDER BY t.label;

WITH target(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
      ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
      ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
      ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
)
SELECT 'PROVIDER_OPERATION' AS section, t.label, row_to_json(o)::text AS row_json
FROM target t
LEFT JOIN dance_sell.dance_sell_provider_operations o
  ON o.dance_sell_job_id = t.dance_sell_job_id
  OR o.render_job_id = t.render_job_id
  OR o.provider_task_id = t.provider_task_id
ORDER BY t.label, o.created_at, o.attempt_no, o.id;

WITH target(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
      ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
      ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
      ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
), ops AS (
    SELECT t.label, o.id AS operation_id
    FROM target t
    JOIN dance_sell.dance_sell_provider_operations o
      ON o.dance_sell_job_id = t.dance_sell_job_id
      OR o.render_job_id = t.render_job_id
      OR o.provider_task_id = t.provider_task_id
)
SELECT 'OPERATION_ASSET' AS section, ops.label, row_to_json(a)::text AS row_json
FROM ops
JOIN public.todox_ai_operation_assets a ON a.operation_id = ops.operation_id
ORDER BY ops.label, a.created_at, a.asset_role;

WITH target(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
      ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
      ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
      ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
)
SELECT 'RENDER_EVENT' AS section, t.label, row_to_json(e)::text AS row_json
FROM target t
JOIN render.render_job_events e
  ON e.job_id = t.render_job_id
  OR e.render_job_id = t.render_job_id
  OR e.provider_task_id = t.provider_task_id
  OR e.data_json::text ILIKE '%' || t.dance_sell_job_id::text || '%'
  OR e.data_json::text ILIKE '%' || t.provider_task_id || '%'
ORDER BY t.label, e.created_at, e.id;

WITH target(dance_sell_job_id, render_job_id, provider_task_id, label) AS (
    VALUES
      ('23805c3f-1d44-465f-be30-cde61aafdbfe'::uuid, 'eefdf7e4-dc05-4e20-bb62-a3765d3b6d6b'::uuid, '58660402320274b2', 'FAILED_NEW'),
      ('28171031-d86d-4c67-8eff-67aab154df5f'::uuid, '3fe09576-48b5-4e02-ba00-c89ad4a0572f'::uuid, '56a3fb0578396662', 'FAILED_PREVIOUS'),
      ('49554a92-900a-42ec-8f81-543faa0bfe95'::uuid, 'c95e0823-32e7-4e27-8752-9ac9bf73a607'::uuid, '842d3440df5430b8', 'SUCCESS')
), media_ids AS (
    SELECT t.label, j.character_media_id AS media_id FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id=t.dance_sell_job_id
    UNION ALL SELECT t.label, j.product_media_id FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id=t.dance_sell_job_id
    UNION ALL SELECT t.label, j.direct_reference_media_id FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id=t.dance_sell_job_id
    UNION ALL SELECT t.label, j.motion_media_id FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id=t.dance_sell_job_id
    UNION ALL SELECT t.label, j.motion_video_media_id FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id=t.dance_sell_job_id
    UNION ALL SELECT t.label, j.prepared_reference_media_id FROM target t JOIN dance_sell.dance_sell_jobs j ON j.id=t.dance_sell_job_id
)
SELECT 'MEDIA_FILE' AS section, m.label, row_to_json(f)::text AS row_json
FROM media_ids m
JOIN media.media_files f ON f.id = m.media_id
WHERE m.media_id IS NOT NULL
ORDER BY m.label, f.created_at, f.id;

ROLLBACK;
```

Old RDance same-input lookup needs a separate read-only query against the old database after the Dashboard source input URL/object key is known:

```sql
BEGIN TRANSACTION READ ONLY;

-- Replace the placeholders with exact values from the Dashboard export.
SELECT *
FROM public.todox_rdance_jobs
WHERE source_video_url = :source_video_url
   OR source_resolved_url = :source_video_url
   OR source_video_79ai_url = :motion_provider_url
   OR character_79ai_url = :reference_provider_url
ORDER BY created_at DESC;

ROLLBACK;
```

## 15. Final Result

No evidence-backed code fix should be made from the currently available local data.

First concrete divergence for target `586604...`: provider accepted the submit, created a task, ran it, then returned terminal `ERROR` with no diagnostic reason. That rejects Dashboard polling and local upload/list verification as the immediate boundary.

Root cause for the requested three-way comparison remains:

**UNKNOWN**

Most useful next step: run the read-only SQL above and provide the exported rows. Then the table can be filled with exact provider account, prompt, URL form, timing, and old-RDance same-input comparison.

