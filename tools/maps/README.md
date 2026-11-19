# TodoX self-hosted map (geo layer)

Map data for EditVideo's geo layer is self-hosted on the production server, outside the deployed site:
`H:\WEBDEPLOYED\todoX\maps` (beside `...\saas`), served by the dashboard at `https://dashboard.todox.vn/maps/...`
(`TodoX.Web/Services/Maps/MapHosting.cs`; override the folder with `Maps:Root`). Dashboard deploys never touch it.

| File | What |
|---|---|
| `vietnam.pmtiles` | Protomaps basemap v4 (OpenStreetMap), bbox `102.1,8.0,117.8,23.45` (incl. Hoàng Sa, Trường Sa), zoom 0–15, ~1.1 GB |
| `fonts/` | Noto Sans Regular/Medium/Italic glyphs (OFL) from `protomaps/basemaps-assets` |
| `sprites/v4/` | Protomaps sprites |
| `styles/todox-dark.json` | TodoX dark style (MapLibre; source `pmtiles://https://dashboard.todox.vn/maps/vietnam.pmtiles`) |

Every map/video must show **© OpenStreetMap contributors**.

## Rebuild / update

```bash
# pick a build from https://build-metadata.protomaps.dev/builds.json
node tools/maps/extract.mjs https://build.protomaps.com/20261118.pmtiles vietnam.pmtiles 102.1,8.0,117.8,23.45 15 --plan
node tools/maps/extract.mjs https://build.protomaps.com/20261118.pmtiles vietnam.pmtiles 102.1,8.0,117.8,23.45 15
node tools/maps/make-style.mjs https://dashboard.todox.vn/maps styles/todox-dark.json
```

`extract.mjs` has no dependencies: it reads only the needed byte ranges of the planet archive and writes a PMTiles v3
file. Upload as `vietnam.pmtiles.part`, check SHA-256 on the server, then rename (readers never see a half file).
