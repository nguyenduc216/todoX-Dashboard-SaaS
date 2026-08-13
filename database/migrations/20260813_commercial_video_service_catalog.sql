-- Commercial video service catalog bootstrap.
-- Safe to run multiple times.
-- Preserves existing catalog.services rows and admin-customized thumbnail/service metadata when already set.
-- Seeds 10 commercial services and editable bootstrap sell prices.

CREATE TABLE IF NOT EXISTS catalog.service_sell_prices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    service_id uuid NOT NULL REFERENCES catalog.services(id) ON DELETE CASCADE,
    asset_type varchar NOT NULL,
    quality_tier varchar NOT NULL,
    duration_seconds numeric NULL,
    sell_points numeric NOT NULL,
    display_label varchar NULL,
    is_active boolean NOT NULL DEFAULT true,
    sort_order integer NOT NULL DEFAULT 0,
    created_by uuid NULL,
    updated_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_service_sell_price_asset_type CHECK (asset_type IN ('image','video_scene')),
    CONSTRAINT ck_service_sell_price_quality_tier CHECK (quality_tier IN ('standard','premium')),
    CONSTRAINT ck_service_sell_price_points CHECK (sell_points >= 0),
    CONSTRAINT ck_service_sell_price_duration CHECK (
        (asset_type = 'image' AND duration_seconds IS NULL)
        OR (asset_type = 'video_scene' AND duration_seconds IS NOT NULL AND duration_seconds > 0)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_catalog_service_sell_prices_identity
    ON catalog.service_sell_prices (service_id, asset_type, quality_tier, (COALESCE(duration_seconds, 0)));

WITH commercial_services(service_code, service_name, short_description, description, service_type, sort_order, thumbnail_key) AS (
    VALUES
        ('CONSTRUCTION_VIDEO','Xây dựng & Công trình','Biến hình ảnh công trình thành video AI chuyên nghiệp, thể hiện quy trình thi công, tiến độ, năng lực đội ngũ và giá trị dự án một cách trực quan.','Dịch vụ phù hợp cho các đơn vị thi công, xây dựng, nhà thầu và đơn vị thiết kế muốn tạo video giới thiệu công trình nhanh chóng bằng AI. Từ hình ảnh thực tế hoặc hình hoàn thiện, hệ thống có thể hỗ trợ tạo video quy trình, video quảng bá dự án, video showcase năng lực thi công và nội dung truyền thông cho thương hiệu ngành xây dựng.','timelapse',10,'nganh-xay-dung'),
        ('BUDDHISM_CONTENT_VIDEO','Phật pháp & Nội dung tu học','Tạo video AI cho nội dung Phật pháp, bài giảng, tu học và lan tỏa giá trị từ bi, trí tuệ một cách gần gũi và truyền cảm.','Dịch vụ dành cho các kênh Phật pháp, tổ chức tu học, truyền thông tâm linh hoặc cộng đồng muốn sản xuất video mang giá trị an lạc, giáo dục và tỉnh thức. Nội dung có thể hướng tới kể chuyện, trích dẫn, chia sẻ giáo lý, video hoạt họa hoặc video cảm hứng để tăng khả năng tiếp cận trên các nền tảng số.','rvideo',20,'nganh-phat-phap'),
        ('HEALTHCARE_VIDEO','Sức khỏe','Xây dựng video AI cho sản phẩm và dịch vụ sức khỏe, giúp truyền tải thông tin dễ hiểu, hấp dẫn và tăng niềm tin khách hàng.','Phù hợp với sản phẩm chăm sóc sức khỏe, giáo dục sức khỏe, nhà thuốc, phòng khám hoặc nội dung truyền thông cộng đồng. Hệ thống giúp biến kiến thức chuyên môn thành video trực quan, dễ xem, dễ nhớ; từ đó nâng cao hiệu quả truyền thông, tăng khả năng giữ chân người xem và thúc đẩy chuyển đổi.','rvideo',30,'nganh-suc-khoe'),
        ('COSMETICS_VIDEO','Mỹ phẩm','Tạo video mỹ phẩm cuốn hút theo phong cách review, giới thiệu sản phẩm, beauty content và social commerce.','Dịch vụ phù hợp cho thương hiệu mỹ phẩm, spa, cửa hàng beauty và người bán hàng online muốn tạo video bắt mắt, hiện đại và có tính chuyển đổi cao. Nội dung có thể tập trung vào trải nghiệm sản phẩm, demo công dụng, cảm nhận người dùng, video ngắn viral hoặc video bán hàng tối ưu cho TikTok, Reels và Facebook.','rvideo',40,'nganh-my-pham'),
        ('FASHION_VIDEO','Thời trang','Biến hình ảnh sản phẩm thời trang thành video AI sinh động, phù hợp cho lookbook, bán hàng và quảng bá thương hiệu.','Dành cho shop thời trang, thương hiệu quần áo, xưởng may và nhà bán hàng muốn tạo nội dung giới thiệu sản phẩm nổi bật hơn. Có thể triển khai video lookbook, mix & match, catwalk, review outfit, nội dung mùa vụ hoặc các video ngắn phục vụ bán hàng đa nền tảng.','rdance',50,'nganh-thoi-trang'),
        ('FOOD_SNACK_VIDEO','Ẩm thực & Đồ ăn vặt','Tạo video AI hấp dẫn cho món ăn, đồ uống và đồ ăn vặt, giúp nội dung bắt mắt hơn và tăng khả năng thu hút khách hàng.','Phù hợp với nhà hàng, quán ăn, thương hiệu đồ uống, đồ ăn vặt hoặc người bán hàng online muốn tạo video ngon mắt, kích thích người xem. Nội dung có thể là giới thiệu món, quay dựng sản phẩm, menu nổi bật, combo ưu đãi, video bắt trend hoặc video social commerce phục vụ bán hàng.','rvideo',60,'nganh-am-thuc-do-an-vat'),
        ('ETHICAL_KNOWLEDGE_VIDEO','Video kiến thức đạo lý','Sản xuất video AI truyền cảm hứng về đạo lý sống, tri thức, tư duy tích cực và các giá trị nhân văn.','Dịch vụ hướng tới các kênh chia sẻ tri thức, phát triển bản thân, giáo dục giá trị sống và truyền thông định hướng tích cực. Nội dung có thể là bài học ngắn, video trích dẫn, kể chuyện, tư duy sống đẹp, truyền cảm hứng hoặc xây dựng kênh nội dung giáo dục giá trị bền vững.','rvideo',70,'video-kien-thuc-dao-ly'),
        ('REAL_ESTATE_VIDEO','Bất động sản','Tạo video AI cho nhà đất, dự án và sản phẩm bất động sản, giúp hình ảnh chuyên nghiệp hơn và tăng hiệu quả tiếp cận khách hàng.','Dành cho môi giới, sàn giao dịch, chủ đầu tư hoặc đội nhóm truyền thông bất động sản muốn tạo video giới thiệu dự án, nhà mẫu, tiện ích, quy hoạch, lifestyle và nội dung bán hàng. Hệ thống giúp nội dung rõ ràng, sinh động và thuận tiện khi triển khai trên nhiều nền tảng.','rvideo',80,'nganh-bat-dong-san'),
        ('LIVESTREAM_MODEL_VIDEO','Livestream - Người mẫu','Hỗ trợ tạo video AI cho livestream bán hàng, review sản phẩm và hình thức nội dung có người mẫu nhằm tăng tương tác và chuyển đổi.','Dịch vụ phù hợp cho các lĩnh vực cần nhân vật đại diện, livestreamer, người mẫu hoặc creator để tăng tính cảm xúc và tỷ lệ chốt đơn. Nội dung có thể là demo sản phẩm, review, kịch bản livestream, video pre-live, video cut từ livestream và nội dung social commerce thiên về bán hàng.','rdance',90,'nganh-livestream-nguoi-mau'),
        ('PERSONAL_BRAND_CHANNEL_VIDEO','Xây kênh nhãn hiệu','Xây dựng hệ thống video AI phục vụ phát triển thương hiệu cá nhân, thương hiệu doanh nghiệp và kênh nội dung dài hạn.','Dịch vụ dành cho cá nhân, chuyên gia, doanh nghiệp và creator muốn xây dựng kênh nội dung có định hướng rõ ràng. Hệ thống giúp tạo video đều đặn, nhất quán về hình ảnh và thông điệp, từ đó tăng độ nhận diện, gây dựng uy tín và mở rộng tệp khách hàng bền vững trên nhiều nền tảng.','rvideo',100,'xay-kenh-nhan-hieu')
)
INSERT INTO catalog.services (id, category_id, service_code, service_name, service_type, description, short_description, thumbnail_url, cover_image_url, workflow_code, status, sort_order, created_at, updated_at)
SELECT
    COALESCE(s.id, gen_random_uuid()),
    s.category_id,
    c.service_code,
    c.service_name,
    c.service_type,
    c.description,
    c.short_description,
    s.thumbnail_url,
    s.cover_image_url,
    s.workflow_code,
    COALESCE(NULLIF(s.status, ''), 'enabled'),
    c.sort_order,
    COALESCE(s.created_at, now()),
    now()
FROM commercial_services c
LEFT JOIN catalog.services s ON lower(s.service_code) = lower(c.service_code)
ON CONFLICT (service_code) DO UPDATE SET
    category_id = catalog.services.category_id,
    service_name = catalog.services.service_name,
    service_type = catalog.services.service_type,
    description = catalog.services.description,
    short_description = catalog.services.short_description,
    thumbnail_url = COALESCE(NULLIF(catalog.services.thumbnail_url, ''), EXCLUDED.thumbnail_url),
    cover_image_url = COALESCE(NULLIF(catalog.services.cover_image_url, ''), EXCLUDED.cover_image_url),
    workflow_code = catalog.services.workflow_code,
    status = catalog.services.status,
    sort_order = catalog.services.sort_order,
    updated_at = now();

WITH service_ids AS (
    SELECT id, service_code FROM catalog.services WHERE service_code IN (
        'CONSTRUCTION_VIDEO','BUDDHISM_CONTENT_VIDEO','HEALTHCARE_VIDEO','COSMETICS_VIDEO','FASHION_VIDEO',
        'FOOD_SNACK_VIDEO','ETHICAL_KNOWLEDGE_VIDEO','REAL_ESTATE_VIDEO','LIVESTREAM_MODEL_VIDEO','PERSONAL_BRAND_CHANNEL_VIDEO'
    )
), price_seed AS (
    SELECT * FROM (VALUES
        ('image','standard',NULL::numeric,3,'3 điểm / hình',10),
        ('image','premium',NULL::numeric,5,'5 điểm / hình',20),
        ('video_scene','standard',4::numeric,8,'8 điểm / scene 4 giây',30),
        ('video_scene','standard',6::numeric,10,'10 điểm / scene 6 giây',40),
        ('video_scene','standard',8::numeric,12,'12 điểm / scene 8 giây',50),
        ('video_scene','premium',4::numeric,12,'12 điểm / scene 4 giây',60),
        ('video_scene','premium',6::numeric,15,'15 điểm / scene 6 giây',70),
        ('video_scene','premium',8::numeric,18,'18 điểm / scene 8 giây',80)
    ) AS p(asset_type, quality_tier, duration_seconds, sell_points, display_label, sort_order)
)
INSERT INTO catalog.service_sell_prices (service_id, asset_type, quality_tier, duration_seconds, sell_points, display_label, is_active, sort_order, created_at, updated_at)
SELECT
    s.id,
    p.asset_type,
    p.quality_tier,
    p.duration_seconds,
    p.sell_points,
    p.display_label,
    true,
    p.sort_order,
    now(),
    now()
FROM service_ids s
CROSS JOIN price_seed p
ON CONFLICT (service_id, asset_type, quality_tier, (COALESCE(duration_seconds, 0)))
DO UPDATE SET
    display_label = COALESCE(catalog.service_sell_prices.display_label, EXCLUDED.display_label),
    is_active = catalog.service_sell_prices.is_active,
    sort_order = EXCLUDED.sort_order,
    updated_at = now();
