-- TodoX RVideo: videos still titled "Video mới" (the draft default) get a short title from their content, the same
-- rules as TodoX.Web/Services/VideoRender/RVideoTitleRules.cs (new videos are titled by the app itself):
--   1. the script's own title: video_title / title / meta.product_name / meta.video_title
--   2. else the description the customer typed (render_jobs.input_json->>'draftInput')
--   3. else a plain-text prompt
-- then first line, first sentence, at most 60 characters cut at a word (+ "…").
-- Data only (render.render_jobs.input_json title + video_render.video_projects.title); idempotent: a video that
-- already has a real title, or has no content yet, is left untouched. No schema change.

create or replace function pg_temp.todox_short_title(src text) returns text
language plpgsql immutable as $fn$
declare
    s text;
    sentence text;
begin
    if src is null or btrim(src) = '' then return null; end if;
    select l into s from regexp_split_to_table(replace(src, chr(13), ''), E'\n') l where btrim(l) <> '' limit 1;
    if s is null then return null; end if;
    s := regexp_replace(s, '^\s*(?:[-*•#>]+|\d+[.)])\s*', '');
    s := btrim(btrim(regexp_replace(s, '\s+', ' ', 'g')), '"“”''');
    s := btrim(s);
    sentence := (regexp_match(s, '^(.+?[.!?])(\s|$)'))[1];
    if sentence is not null and length(sentence) >= 10 then s := sentence; end if;
    s := rtrim(s, '. ');
    if length(s) > 60 then
        s := coalesce(nullif(regexp_replace(left(s, 60), '\s+\S*$', ''), ''), left(s, 60));
        s := rtrim(s, ',;:- ') || '…';
    end if;
    if s = '' or lower(s) in ('video mới', 'prompt workspace') or s ~ '^[\[{]' then return null; end if;
    return s;
end
$fn$;

create or replace function pg_temp.todox_title_from_content(prompt text, draft text) returns text
language plpgsql immutable as $fn$
declare
    p text := btrim(coalesce(prompt, ''));
    d text := btrim(coalesce(draft, ''));
    doc jsonb;
    t text;
begin
    if p ~ '^[\[{]' then
        begin
            doc := p::jsonb;
        exception when others then
            doc := null;
        end;
        if doc is not null and jsonb_typeof(doc) = 'object' then
            t := pg_temp.todox_short_title(coalesce(
                nullif(btrim(case when jsonb_typeof(doc->'video_title') = 'string' then doc->>'video_title' end), ''),
                nullif(btrim(case when jsonb_typeof(doc->'title') = 'string' then doc->>'title' end), ''),
                nullif(btrim(case when jsonb_typeof(doc->'meta') = 'object' and jsonb_typeof(doc->'meta'->'product_name') = 'string' then doc->'meta'->>'product_name' end), ''),
                nullif(btrim(case when jsonb_typeof(doc->'meta') = 'object' and jsonb_typeof(doc->'meta'->'video_title') = 'string' then doc->'meta'->>'video_title' end), '')));
            if t is not null then return t; end if;
        end if;
    end if;
    if d <> '' and d !~ '^[\[{]' then
        t := pg_temp.todox_short_title(d);
        if t is not null then return t; end if;
    end if;
    if p <> '' and p !~ '^[\[{]' then return pg_temp.todox_short_title(p); end if;
    return null;
end
$fn$;

create temp table todox_rvideo_new_titles on commit drop as
select j.id as job_id, p.id as project_id,
       pg_temp.todox_title_from_content(p.original_prompt, j.input_json->>'draftInput') as title
  from render.render_jobs j
  join video_render.video_projects p on p.core_job_id = j.id and p.tenant_id = j.tenant_id
 where (j.input_json->>'title' is null or btrim(j.input_json->>'title') = '' or lower(btrim(j.input_json->>'title')) in ('video mới', 'prompt workspace') or btrim(j.input_json->>'title') ~ '^[\[{]')
   and (p.title is null or btrim(p.title) = '' or lower(btrim(p.title)) in ('video mới', 'prompt workspace') or btrim(p.title) ~ '^[\[{]');

delete from todox_rvideo_new_titles where title is null;

update render.render_jobs j
   set input_json = jsonb_set(coalesce(j.input_json, '{}'::jsonb), '{title}', to_jsonb(n.title))
  from todox_rvideo_new_titles n
 where j.id = n.job_id;

update video_render.video_projects p
   set title = n.title
  from todox_rvideo_new_titles n
 where p.id = n.project_id;
