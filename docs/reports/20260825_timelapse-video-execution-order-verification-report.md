# Timelapse Video Execution Order Verification Report

Date: 2026-08-25  
Repository: `nguyenduc216/todoX-Dashboard-SaaS`  
Branch: `integration/rdance-on-construction-video-core`  
Verified commit: `ac1ea3ae0841ff7ecd015c3f183ca9d94e3ea706`

## Scope

This report verifies the Timelapse image dependency order, video clip activation order, video worker claiming behavior, and finalizer merge order.

No production code was modified during this verification.

## 1. Stage Graph

Source: `TodoX.Web/Models/Timelapse/TimelapseModels.cs`, `TimelapseStageGraphBuilder.Build`.

The graph creates video edges from adjacent progress values in ascending order:

```csharp
var clips = images.Zip(images.Skip(1), (start, end) => new { start, end })
    .Select((x, index) => new TimelapseVideoEdge(index + 1, x.start, x.end));
```

Generated image stages are ordered in descending progress:

```csharp
var generatedOrder = images
    .Where(x => x < 100)
    .OrderByDescending(x => x)
    .ToArray();
```

For the 5-scene graph:

| Type | Order |
|---|---|
| Image generation | `80, 60, 40, 20, 0` |
| Clip 1 | `0 -> 20` |
| Clip 2 | `20 -> 40` |
| Clip 3 | `40 -> 60` |
| Clip 4 | `60 -> 80` |
| Clip 5 | `80 -> 100` |

Clip indexes are assigned from the ascending progress pairs and start at `1`.

## 2. Image Completion Advancement

Sources:

- `TodoX.Web/Services/Timelapse/TimelapseProviderRuntime.cs`, `ProcessImageAsync`
- `TodoX.Web/Services/Timelapse/TimelapseWorkerRepository.cs`, `AdvanceAfterImageCompletedAsync`
- `TodoX.Web/Services/Timelapse/TimelapseWorkerRepository.cs`, `StartNextImageIfReadyAsync`

After an image completes, the runtime calls:

```csharp
await _repo.AdvanceAfterImageCompletedAsync(item.JobId, ct);
```

`AdvanceAfterImageCompletedAsync` calls `StartNextImageIfReadyAsync`, which selects the next image using:

```sql
WHERE s.job_id=@jobId
  AND s.is_original=false
  AND s.status IN ('WAITING','FAILED','INVALIDATED')
  AND (
      s.depends_on_progress_percent IS NULL
      OR (
          d.status='COMPLETED'
          AND d.result_media_id IS NOT NULL
          AND (NULLIF(d.public_url,'') IS NOT NULL OR NULLIF(d.object_key,'') IS NOT NULL)
      )
  )
ORDER BY s.progress_percent DESC
LIMIT 1
```

After selecting the next image, it calls `StartReadyVideosAsync`.

For the 5-scene graph, the corresponding video activation is:

| Completed image | Clip activated as `RENDERING` |
|---:|---|
| 80 | clip 5: `80 -> 100` |
| 60 | clip 4: `60 -> 80` |
| 40 | clip 3: `40 -> 60` |
| 20 | clip 2: `20 -> 40` |
| 0 | clip 1: `0 -> 20` |

## 3. `ClaimVideoAsync` Eligibility

Source: `TodoX.Web/Services/Timelapse/TimelapseWorkerRepository.cs`, `ClaimVideoAsync`.

Candidate rows must satisfy:

```sql
WHERE c.tenant_id=@tenant
  AND c.status='RENDERING'
  AND v.status='RENDERING'
  AND COALESCE(
        (v.request_json->'worker_claim'->>'until')::timestamptz,
        '-infinity'::timestamptz
      ) <= now()
```

Claim ordering is:

```sql
ORDER BY c.started_at NULLS FIRST, c.clip_index
LIMIT 1
FOR UPDATE SKIP LOCKED
```

The returned work item resolves the current completed start and end image stages using:

```sql
start_img.progress_percent=c.start_progress_percent
AND start_img.status='COMPLETED'

end_img.progress_percent=c.end_progress_percent
AND end_img.status='COMPLETED'
```

## 4. Can Video Clips Run Simultaneously?

Yes.

`StartReadyVideosAsync` selects every eligible clip with:

```sql
WHERE c.job_id=@jobId
  AND c.status IN ('WAITING','INVALIDATED')
...
ORDER BY c.clip_index
```

It then loops through all returned clips and updates each one to `RENDERING`.

Therefore multiple video clips can be `RENDERING` at the same time. `ClaimVideoAsync` claims one row per worker iteration, while the video worker uses configured video parallelism.

The code activates eligible clips in reverse dependency availability order, but does not enforce strict one-at-a-time provider execution.

## 5. Actual Video Execution Order

The logical activation sequence for the 5-scene job is:

```text
80 -> 100
60 -> 80
40 -> 60
20 -> 40
0 -> 20
```

However, strict provider submit/completion order is not guaranteed because clips may overlap in `RENDERING` and are processed by workers concurrently. Actual timing can vary with worker scheduling, configured parallelism, claim timing, and provider latency.

## 6. Finalizer Order

Source: `TodoX.Web/Services/Timelapse/TimelapseWorkerRepository.cs`, `TryStartFinalizerIfReadyAsync`.

The finalizer loads completed clips using:

```sql
SELECT clip_index AS ClipIndex,
       start_progress_percent AS StartProgressPercent,
       end_progress_percent AS EndProgressPercent,
       duration_seconds AS DurationSeconds,
       video_mode AS VideoMode,
       ratio AS Ratio
  FROM timelapse.timelapse_video_clips
 WHERE tenant_id=@tenant
   AND job_id=@jobId
   AND status='COMPLETED'
 ORDER BY clip_index;
```

Final merge order is therefore:

```text
0 -> 20
20 -> 40
40 -> 60
60 -> 80
80 -> 100
```

## 7. Conclusion

The verified behavior is:

- image generation: reverse dependency order;
- video activation: reverse dependency availability order;
- video execution: potentially concurrent, not strictly serialized;
- finalizer merge: ascending clip index and chronological forward order.

No incorrect start/end clip mapping was found.

The exact method that permits multiple video clips to execute concurrently is:

```text
TimelapseWorkerRepository.StartReadyVideosAsync
```

The relevant SQL is the query selecting all eligible clips with `status IN ('WAITING','INVALIDATED')`, followed by the loop that updates every selected clip to `RENDERING`.

## 8. Verification Status

- Code modified: no.
- Commit modified: no.
- Working tree before report export: clean.
- Verification commit: `ac1ea3ae0841ff7ecd015c3f183ca9d94e3ea706`.

