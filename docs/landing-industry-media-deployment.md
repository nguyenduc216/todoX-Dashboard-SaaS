# Landing industry media deployment

Dashboard page: `/landing/industries`

Landing public API: `/api/industry-solutions`

## Production media folder

The Dashboard upload page writes thumbnail/video files to the folder configured by:

```text
LandingIndustryMedia__RootPath
```

Set this value to the physical `wwwroot/media/industries` directory of the deployed `todox.vn` Landing application.

Example only:

```text
D:\Sites\todox.vn\wwwroot\media\industries
```

Do not store this server-specific path in source control if deployment paths differ between environments.

The database stores public URLs in this form:

```text
/media/industries/<generated-file-name>.mp4
/media/industries/<generated-file-name>.webp
```

Therefore Dashboard and Landing stay independently published, while uploaded industry media is served by `todox.vn`.

## Deployment order

1. Run `database/migrations/20260809_landing_industry_solutions.sql` manually.
2. Publish `TodoX.Landing`.
3. Ensure `<landing physical root>/wwwroot/media/industries` exists and is writable by the Dashboard application pool identity.
4. Set `LandingIndustryMedia__RootPath` for the Dashboard IIS application.
5. Recycle the Landing and Dashboard application pools.
6. Open Dashboard > Landing Page > Giải pháp ngành nghề.
7. Upload a thumbnail and video, save, then confirm the card appears on `https://todox.vn`.

## Recommended media

- Thumbnail: JPG/PNG/WEBP, recommended 1200×1500 for portrait video cards.
- Video: MP4 H.264 preferred; WEBM/MOV accepted by the upload UI.
- Aspect ratio is explicitly stored as `9:16` or `16:9`; the public popup changes layout automatically.
