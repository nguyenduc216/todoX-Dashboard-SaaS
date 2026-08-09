-- TodoX Landing - Industry Solutions Management
-- Run manually. Application code does NOT auto-migrate.
BEGIN;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS landing;
CREATE SCHEMA IF NOT EXISTS system;

CREATE TABLE IF NOT EXISTS landing.industry_solutions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    slug varchar(120) NOT NULL UNIQUE,
    title varchar(160) NOT NULL,
    short_description varchar(500) NULL,
    description text NULL,
    thumbnail_url text NULL,
    video_url text NULL,
    aspect_ratio varchar(10) NOT NULL DEFAULT '9:16',
    display_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_by uuid NULL,
    CONSTRAINT ck_industry_solutions_title_not_blank CHECK (length(btrim(title)) > 0),
    CONSTRAINT ck_industry_solutions_slug_not_blank CHECK (length(btrim(slug)) > 0),
    CONSTRAINT ck_industry_solutions_aspect_ratio CHECK (aspect_ratio IN ('9:16','16:9'))
);

CREATE INDEX IF NOT EXISTS ix_industry_solutions_active_order
ON landing.industry_solutions(is_active, display_order, title);

CREATE OR REPLACE FUNCTION landing.set_industry_solution_updated_at()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_industry_solutions_updated_at ON landing.industry_solutions;
CREATE TRIGGER trg_industry_solutions_updated_at
BEFORE UPDATE ON landing.industry_solutions
FOR EACH ROW EXECUTE FUNCTION landing.set_industry_solution_updated_at();

INSERT INTO landing.industry_solutions
(slug,title,short_description,description,thumbnail_url,video_url,aspect_ratio,display_order,is_active)
VALUES
('suc-khoe','Sức khỏe','Video sản phẩm, thành phần, trải nghiệm và câu chuyện chăm sóc sức khỏe.','TodoX xây dựng video ngắn và nội dung AI phù hợp cho sản phẩm sức khỏe, tập trung vào trải nghiệm, thông tin sản phẩm và khả năng chuyển đổi trên TikTok, Reels, Shorts và các kênh bán hàng.','/img/landing/sneakers.jpg',NULL,'9:16',10,true),
('my-pham','Mỹ phẩm','Review, skincare, sản phẩm cao cấp và video hình ảnh thương hiệu.','Giải pháp video mỹ phẩm có thể triển khai theo phong cách UGC, cinematic product, review skincare, AI presenter và social ads.','/img/landing/cosmetics.jpg',NULL,'9:16',20,true),
('thoi-trang','Thời trang','Lookbook, catwalk, phối đồ và video chuyển động theo xu hướng.','TodoX hỗ trợ lookbook AI, catwalk, motion control, phối đồ và video social commerce tối ưu cho trải nghiệm xem trên thiết bị di động.','/img/landing/fashion.jpg',NULL,'9:16',30,true),
('xay-dung','Xây dựng','Construction timelapse, giới thiệu công trình và mô phỏng dự án.','Giải pháp cho ngành xây dựng gồm construction timelapse, before/after, mô phỏng tiến độ, giới thiệu công trình và project showcase.','/img/landing/construction.jpg',NULL,'16:9',40,true),
('noi-that','Nội thất','Trình diễn không gian, vật liệu, showroom và phong cách sống.','TodoX triển khai interior walkthrough, before/after, showroom video, lifestyle visual và nội dung giới thiệu vật liệu hoặc không gian.','/img/landing/interior.jpg',NULL,'9:16',50,true)
ON CONFLICT (slug) DO UPDATE
SET title=EXCLUDED.title,
    short_description=EXCLUDED.short_description,
    description=EXCLUDED.description,
    thumbnail_url=COALESCE(landing.industry_solutions.thumbnail_url,EXCLUDED.thumbnail_url),
    aspect_ratio=EXCLUDED.aspect_ratio,
    display_order=EXCLUDED.display_order,
    updated_at=now();

INSERT INTO system.navigation_menu_groups
(code,title,icon_key,sort_order,default_expanded,is_active,description)
VALUES
('landing_page','Landing Page','Language',55,true,true,'Quản lý nội dung và khách hàng từ website todox.vn')
ON CONFLICT (code) DO UPDATE
SET title=EXCLUDED.title,icon_key=EXCLUDED.icon_key,sort_order=EXCLUDED.sort_order,
    default_expanded=EXCLUDED.default_expanded,is_active=true,description=EXCLUDED.description,updated_at=now();

WITH landing_group AS
(
    SELECT id FROM system.navigation_menu_groups WHERE code='landing_page'
)
INSERT INTO system.navigation_menu_items
(group_id,code,title,href,icon_key,module_keys,visibility_policy,match_all,sort_order,is_active,description)
SELECT id,'landing_industries','Giải pháp ngành nghề','/landing/industries','VideoLibrary',ARRAY[]::text[],'always',false,20,true,
       'Quản lý ngành nghề, mô tả, thumbnail và video clip hiển thị trên todox.vn'
FROM landing_group
ON CONFLICT (code) DO UPDATE
SET group_id=EXCLUDED.group_id,title=EXCLUDED.title,href=EXCLUDED.href,icon_key=EXCLUDED.icon_key,
    module_keys=EXCLUDED.module_keys,visibility_policy=EXCLUDED.visibility_policy,match_all=EXCLUDED.match_all,
    sort_order=EXCLUDED.sort_order,is_active=true,description=EXCLUDED.description,updated_at=now();

COMMIT;

SELECT id,slug,title,aspect_ratio,display_order,is_active,thumbnail_url,video_url
FROM landing.industry_solutions ORDER BY display_order,title;
