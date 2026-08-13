# Commercial Thumbnail Manifest

Status: runtime did not have access to the 10 uploaded image files from the ChatGPT conversation.

Mapping keys:
- CONSTRUCTION_VIDEO -> nganh-xay-dung
- BUDDHISM_CONTENT_VIDEO -> nganh-phat-phap
- HEALTHCARE_VIDEO -> nganh-suc-khoe
- COSMETICS_VIDEO -> nganh-my-pham
- FASHION_VIDEO -> nganh-thoi-trang
- FOOD_SNACK_VIDEO -> nganh-am-thuc-do-an-vat
- ETHICAL_KNOWLEDGE_VIDEO -> video-kien-thuc-dao-ly
- REAL_ESTATE_VIDEO -> nganh-bat-dong-san
- LIVESTREAM_MODEL_VIDEO -> nganh-livestream-nguoi-mau
- PERSONAL_BRAND_CHANNEL_VIDEO -> xay-kenh-nhan-hieu

Deployment note:
- No replacement images were generated.
- No fake URLs were written.
- Seed migration preserves existing thumbnail_url / cover_image_url values and only fills them when a bundled asset is available in the deployment runtime.
- Admin can upload/replace thumbnails through ServiceDialog using SystemImageStorage.SaveServiceThumbnailAsync.
