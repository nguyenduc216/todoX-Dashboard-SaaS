do $$
declare
    left_over int;
begin
    -- No video that has usable content is still called "Video mới".
    select count(*) into left_over
      from render.render_jobs j
      join video_render.video_projects p on p.core_job_id = j.id and p.tenant_id = j.tenant_id
     where lower(btrim(coalesce(j.input_json->>'title', ''))) in ('', 'video mới')
       and lower(btrim(coalesce(p.title, ''))) in ('', 'video mới')
       and pg_temp.todox_title_from_content(p.original_prompt, j.input_json->>'draftInput') is not null;
    if left_over > 0 then
        raise exception '% videos with content are still titled "Video mới"', left_over;
    end if;

    -- The rules: script title first, then the typed description; short, cut at a word.
    if pg_temp.todox_title_from_content('{"video_title":"Đạo lý người quê: thương cha mẹ"}', 'abc') <> 'Đạo lý người quê: thương cha mẹ' then
        raise exception 'script title not used';
    end if;
    if pg_temp.todox_title_from_content('{"scenes":[]}', 'Review homestay Rừng lá kim Đà Lạt cho gia đình. Có cảnh sáng sớm.')
       <> 'Review homestay Rừng lá kim Đà Lạt cho gia đình' then
        raise exception 'description not shortened to its first sentence';
    end if;
    if length(pg_temp.todox_short_title(repeat('chữ ', 40))) > 61 then
        raise exception 'title longer than 60 characters';
    end if;
    if pg_temp.todox_title_from_content(null, '') is not null then
        raise exception 'an empty video must keep its draft title';
    end if;
end $$;
