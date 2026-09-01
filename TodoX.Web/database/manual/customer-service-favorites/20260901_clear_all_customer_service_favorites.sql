-- Manual cleanup for environments where the old migration backfilled every active service.
-- Result: every customer account starts with an empty favorite-service list.
-- Admin can then configure favorites per account; users can add favorites from Kho dịch vụ video.

BEGIN;

DELETE FROM crm.customer_service_favorites;

COMMIT;

-- Optional verification:
-- SELECT count(*) AS favorite_count FROM crm.customer_service_favorites;
-- Expected after cleanup: 0
