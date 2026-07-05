BEGIN;

INSERT INTO system.app_settings
    (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
VALUES
    (
        gen_random_uuid(),
        'todox.avatar.mr_todox_url',
        'branding',
        'string',
        '',
        'URL hình ảnh đại diện Mr. todoX dùng làm ảnh tham chiếu khi render thumbnail dịch vụ.',
        true,
        now()
    ),
    (
        gen_random_uuid(),
        'todox.avatar.mr_todox_prompt',
        'branding',
        'text',
        'Mr. todoX is the official TodoX mascot/avatar: a friendly futuristic AI assistant, modern SaaS consultant style, professional, trustworthy, energetic, with TodoX brand colors using dark navy, white and gold/yellow accent. The character should appear naturally in the service thumbnail as a guide or presenter.',
        'Mô tả fallback của nhân vật Mr. todoX khi render ảnh không có reference image.',
        true,
        now()
    )
ON CONFLICT (setting_key) DO NOTHING;

COMMIT;
