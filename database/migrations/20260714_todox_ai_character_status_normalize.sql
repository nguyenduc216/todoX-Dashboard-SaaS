-- Chuẩn hóa cột status của public.todox_ai_character.
-- Idempotent: chạy lại nhiều lần vẫn an toàn.
-- Bối cảnh: bug BuildParams đè status = NULL khiến toàn bộ bản ghi có status NULL
-- (đã xác nhận qua chẩn đoán: 5/5 rows NULL). Migration này dọn dữ liệu cũ và siết
-- ràng buộc để tránh tái diễn. Chạy trên database todo_saas.
--
-- Schema thực tế tại thời điểm viết:
--   - status: varchar(250), nullable=YES, default 'active'
--   - CHECK 'chk_todox_ai_character_status' = status IN ('active','inactive') ĐÃ tồn tại
--     (nhưng NULL lọt qua do logic 3-trạng-thái -> cần SET NOT NULL)

BEGIN;

-- 1. Dọn dữ liệu cũ về đúng hai giá trị 'active' / 'inactive'.
UPDATE public.todox_ai_character
SET status = CASE
    WHEN lower(trim(coalesce(status, ''))) = 'inactive' THEN 'inactive'
    ELSE 'active'
END
WHERE status IS NULL
   OR trim(status) = ''
   OR status <> lower(trim(status))
   OR lower(trim(status)) NOT IN ('active', 'inactive');

-- 2. Đảm bảo không còn NULL trước khi đặt NOT NULL.
UPDATE public.todox_ai_character
SET status = 'active'
WHERE status IS NULL;

-- 3. Default (đã là 'active' - no-op an toàn) + NOT NULL (cần thiết vì cột đang nullable).
ALTER TABLE public.todox_ai_character
    ALTER COLUMN status SET DEFAULT 'active';

ALTER TABLE public.todox_ai_character
    ALTER COLUMN status SET NOT NULL;

-- 4. CHECK constraint idempotent. Constraint 'chk_todox_ai_character_status' đã tồn tại
--    với đúng định nghĩa status IN ('active','inactive') nên chỉ tạo khi thiếu.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'public.todox_ai_character'::regclass
           AND conname = 'chk_todox_ai_character_status'
    ) THEN
        ALTER TABLE public.todox_ai_character
            ADD CONSTRAINT chk_todox_ai_character_status
            CHECK (status IN ('active', 'inactive'));
    END IF;
END $$;

COMMIT;
