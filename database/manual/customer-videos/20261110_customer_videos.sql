-- TodoX: videos TodoX produces for a customer ("Video truyền thông", "Video TVC doanh nghiệp").
-- An admin sets the point price of each kind, then adds a finished video to a customer account; the
-- customer is charged through the existing point wallet (billing.token_transactions, reference_type
-- 'customer_video') and sees the video, its note and its script under "Jobs của tôi".
-- Additive and idempotent: new schema studio only, no Foundation V2 object is touched.

create schema if not exists studio;

create table if not exists studio.video_product_prices (
    product_code text primary key,
    name         text not null,
    points       numeric(18,2) not null default 0,
    description  text,
    is_active    boolean not null default true,
    sort_order   integer not null default 0,
    updated_at   timestamptz not null default now(),
    updated_by   uuid,
    constraint ck_video_product_prices_code check (product_code in ('truyen_thong', 'tvc')),
    constraint ck_video_product_prices_points check (points >= 0)
);

insert into studio.video_product_prices (product_code, name, points, sort_order)
values ('truyen_thong', 'Video truyền thông', 0, 10),
       ('tvc', 'Video TVC doanh nghiệp', 0, 20)
on conflict (product_code) do nothing;

create table if not exists studio.customer_videos (
    id             uuid primary key,
    customer_id    uuid not null,
    product_code   text not null references studio.video_product_prices (product_code),
    title          text not null,
    video_url      text,
    poster_url     text,
    note           text,
    script         text,
    charged_points numeric(18,2) not null default 0,
    charge_status  text not null default 'pending',
    created_at     timestamptz not null default now(),
    created_by     uuid,
    updated_at     timestamptz not null default now(),
    updated_by     uuid,
    constraint ck_customer_videos_charge_status check (charge_status in ('pending', 'charged', 'free', 'refunded')),
    constraint ck_customer_videos_points check (charged_points >= 0)
);

create index if not exists ix_customer_videos_customer_created
    on studio.customer_videos (customer_id, created_at desc);
