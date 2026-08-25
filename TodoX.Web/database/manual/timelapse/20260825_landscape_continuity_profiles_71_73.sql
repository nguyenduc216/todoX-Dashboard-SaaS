-- Manual, idempotent profile JSON update for Timelapse landscape profiles 7A, 7B and 7C.
-- Run in the automation database. This script does not alter schema or provider routing.

-- Verification before update.
SELECT select_no, profile_code, profile_name, profile_json
  FROM public.todox_timelapse_prompt_profiles
 WHERE select_no IN (71, 72, 73)
 ORDER BY select_no;

WITH landscape_rules(select_no, profile_code, intent, preserve_items, phase_items) AS (
    VALUES
    (
        71,
        'landscape_balcony_install_v1',
        'installation progression',
        jsonb_build_array(
            'same real balcony/garden identity',
            'same architecture, railing, walls, doors, windows, camera and perspective',
            'installed components, flooring/deck, planters, benches, hardscape and permanent fixtures once introduced',
            'immediately adjacent stage as the primary continuity anchor'
        ),
        jsonb_build_array(
            jsonb_build_object('min_progress', 0, 'max_progress', 19, 'phase_goal', 'empty or nearly empty shell; bare balcony/base condition', 'must_exist', jsonb_build_array('same architectural shell', 'bare base condition'), 'must_not_exist', jsonb_build_array('finished decor', 'complete planting layout'), 'preserve_from_adjacent_stage', jsonb_build_array('architecture', 'camera', 'balcony geometry'), 'allowed_changes', jsonb_build_array('remove later installation completion only'), 'forbidden_changes', jsonb_build_array('redesign', 'camera drift')),
            jsonb_build_object('min_progress', 20, 'max_progress', 39, 'phase_goal', 'very early installation with first materials, tools, planters or setup elements and large empty areas', 'must_exist', jsonb_build_array('same shell', 'initial install elements'), 'must_not_exist', jsonb_build_array('fully installed layout'), 'preserve_from_adjacent_stage', jsonb_build_array('architecture', 'camera', 'layout direction'), 'allowed_changes', jsonb_build_array('limited first setup'), 'forbidden_changes', jsonb_build_array('random relocation', 'regression from adjacent stage')),
            jsonb_build_object('min_progress', 40, 'max_progress', 59, 'phase_goal', 'clear installation in progress with more components and a forming layout', 'must_exist', jsonb_build_array('same shell', 'construction/install activity', 'forming layout'), 'must_not_exist', jsonb_build_array('near-finished completion'), 'preserve_from_adjacent_stage', jsonb_build_array('existing install direction', 'major layout'), 'allowed_changes', jsonb_build_array('add phase-appropriate components'), 'forbidden_changes', jsonb_build_array('remove established flooring or planters')),
            jsonb_build_object('min_progress', 60, 'max_progress', 79, 'phase_goal', 'substantial installation progress with stable main layout and more permanent components', 'must_exist', jsonb_build_array('main layout', 'substantial flooring/deck or install components', 'planter/fixture progress'), 'must_not_exist', jsonb_build_array('empty shell appearance'), 'preserve_from_adjacent_stage', jsonb_build_array('installed components', 'flooring/deck progress', 'planter groups'), 'allowed_changes', jsonb_build_array('advance installation'), 'forbidden_changes', jsonb_build_array('less complete next stage', 'random redesign')),
            jsonb_build_object('min_progress', 80, 'max_progress', 100, 'phase_goal', 'near-finished installation converging to the completed supplied image', 'must_exist', jsonb_build_array('most permanent items', 'nearly complete flooring/benches/planters/planting groups'), 'must_not_exist', jsonb_build_array('major unfinished shell reset'), 'preserve_from_adjacent_stage', jsonb_build_array('all established permanent layout'), 'allowed_changes', jsonb_build_array('final decor and refinement'), 'forbidden_changes', jsonb_build_array('regression', 'missing established items'))
        )
    ),
    (
        72,
        'landscape_garden_growth_v1',
        'growth progression',
        jsonb_build_array(
            'same real balcony/garden identity',
            'same architecture, railing, walls, doors, windows, camera and perspective',
            'planting zones, major pot/planter locations, hardscape and growth direction',
            'existing major greenery should not disappear in later stages',
            'immediately adjacent stage as the primary continuity anchor'
        ),
        jsonb_build_array(
            jsonb_build_object('min_progress', 0, 'max_progress', 19, 'phase_goal', 'empty or pre-landscape state', 'must_exist', jsonb_build_array('same shell', 'planting zones as empty guides'), 'must_not_exist', jsonb_build_array('mature planting'), 'preserve_from_adjacent_stage', jsonb_build_array('architecture', 'hardscape', 'camera'), 'allowed_changes', jsonb_build_array('remove later growth only'), 'forbidden_changes', jsonb_build_array('layout redesign')),
            jsonb_build_object('min_progress', 20, 'max_progress', 39, 'phase_goal', 'initial planting in stable zones', 'must_exist', jsonb_build_array('initial planting', 'same planter locations'), 'must_not_exist', jsonb_build_array('mature density'), 'preserve_from_adjacent_stage', jsonb_build_array('planting zones', 'hardscape'), 'allowed_changes', jsonb_build_array('introduce limited plants'), 'forbidden_changes', jsonb_build_array('random pot relocation')),
            jsonb_build_object('min_progress', 40, 'max_progress', 59, 'phase_goal', 'more established planting with the same layout', 'must_exist', jsonb_build_array('established planting direction', 'stable planters'), 'must_not_exist', jsonb_build_array('fully mature growth'), 'preserve_from_adjacent_stage', jsonb_build_array('planting zones', 'major pots'), 'allowed_changes', jsonb_build_array('increase growth and density'), 'forbidden_changes', jsonb_build_array('loss of major greenery')),
            jsonb_build_object('min_progress', 60, 'max_progress', 79, 'phase_goal', 'clear planting layout and stronger greenery', 'must_exist', jsonb_build_array('clear planting zones', 'stronger greenery', 'hardscape'), 'must_not_exist', jsonb_build_array('empty planting areas'), 'preserve_from_adjacent_stage', jsonb_build_array('all established greenery', 'planter locations'), 'allowed_changes', jsonb_build_array('advance maturity'), 'forbidden_changes', jsonb_build_array('growth regression')),
            jsonb_build_object('min_progress', 80, 'max_progress', 100, 'phase_goal', 'almost mature planting close to the final completed image', 'must_exist', jsonb_build_array('near-mature greenery', 'complete planting layout'), 'must_not_exist', jsonb_build_array('major missing planting groups'), 'preserve_from_adjacent_stage', jsonb_build_array('all planting zones and hardscape'), 'allowed_changes', jsonb_build_array('final maturity and refinement'), 'forbidden_changes', jsonb_build_array('regression', 'scene identity drift'))
        )
    ),
    (
        73,
        'landscape_balcony_hybrid_v1',
        'hybrid install and landscape progression',
        jsonb_build_array(
            'same real balcony/garden identity',
            'same architecture, railing, walls, doors, windows, camera and perspective',
            'plants already introduced, completed flooring/deck, installed fixtures and established decor/furniture',
            'both installation state and greenery density must progress monotonically',
            'immediately adjacent stage as the primary continuity anchor'
        ),
        jsonb_build_array(
            jsonb_build_object('min_progress', 0, 'max_progress', 19, 'phase_goal', 'empty shell before installation and landscaping', 'must_exist', jsonb_build_array('same shell', 'bare base'), 'must_not_exist', jsonb_build_array('finished install', 'complete greenery'), 'preserve_from_adjacent_stage', jsonb_build_array('architecture', 'camera'), 'allowed_changes', jsonb_build_array('remove later completion only'), 'forbidden_changes', jsonb_build_array('redesign')),
            jsonb_build_object('min_progress', 20, 'max_progress', 39, 'phase_goal', 'early setup with limited initial install and landscaping elements', 'must_exist', jsonb_build_array('limited setup', 'first install or plants'), 'must_not_exist', jsonb_build_array('near-finished hybrid layout'), 'preserve_from_adjacent_stage', jsonb_build_array('layout direction', 'planting zones'), 'allowed_changes', jsonb_build_array('limited initial elements'), 'forbidden_changes', jsonb_build_array('random relocation')),
            jsonb_build_object('min_progress', 40, 'max_progress', 59, 'phase_goal', 'early hybrid layout forming', 'must_exist', jsonb_build_array('install progress', 'initial greenery', 'forming layout'), 'must_not_exist', jsonb_build_array('complete finish'), 'preserve_from_adjacent_stage', jsonb_build_array('plants already introduced', 'flooring/deck direction'), 'allowed_changes', jsonb_build_array('add coordinated install and landscape progress'), 'forbidden_changes', jsonb_build_array('remove established plants or flooring')),
            jsonb_build_object('min_progress', 60, 'max_progress', 79, 'phase_goal', 'significant install and landscaping progress', 'must_exist', jsonb_build_array('substantial install', 'stable flooring/deck', 'clear greenery layout', 'installed fixtures'), 'must_not_exist', jsonb_build_array('empty shell appearance'), 'preserve_from_adjacent_stage', jsonb_build_array('all established install and greenery'), 'allowed_changes', jsonb_build_array('advance both tracks'), 'forbidden_changes', jsonb_build_array('regression from install state or greenery density')),
            jsonb_build_object('min_progress', 80, 'max_progress', 100, 'phase_goal', 'near-finished hybrid state converging to the completed supplied image', 'must_exist', jsonb_build_array('near-complete install', 'near-mature greenery', 'established decor/furniture'), 'must_not_exist', jsonb_build_array('major missing layout details'), 'preserve_from_adjacent_stage', jsonb_build_array('plants', 'completed flooring/deck', 'installed fixtures'), 'allowed_changes', jsonb_build_array('final refinement'), 'forbidden_changes', jsonb_build_array('regression', 'independent redesign'))
        )
    )
)
UPDATE public.todox_timelapse_prompt_profiles p
   SET profile_json = COALESCE(p.profile_json, '{}'::jsonb)
       || jsonb_build_object(
            'phase_rules', r.phase_items,
            'continuity_rules', jsonb_build_object(
                'must_preserve', r.preserve_items,
                'must_avoid', jsonb_build_array(
                    'full redesign',
                    'camera or perspective drift',
                    'random relocation of major objects',
                    'later stage less complete than previous stage',
                    'independent render identity'
                )
            )
       )
  FROM landscape_rules r
 WHERE p.select_no = r.select_no
   AND p.profile_code = r.profile_code;

-- Verification after update.
SELECT select_no,
       profile_code,
       profile_json->'phase_rules' AS phase_rules,
       profile_json->'continuity_rules' AS continuity_rules
  FROM public.todox_timelapse_prompt_profiles
 WHERE select_no IN (71, 72, 73)
 ORDER BY select_no;
