# TodoX Landing Industry Management Progress

Ngay cap nhat: 2026-08-09

## Da thuc hien

- Landing public API `GET /api/industry-solutions` da chuyen sang repository rieng.
- API chi tra record `is_active = true` va `deleted_at is null`, sap xep `display_order, title`.
- Khi DB/table/chua co schema, API tra `[]` va log server-side, khong lam crash Landing.
- Landing section nganh nghe load du lieu dong, desktop CSS grid, mobile Swiper.
- Card dung thumbnail poster, khong autoplay video trong grid.
- Card khong co video se hien trang thai dang cap nhat va khong mo modal rong.
- Popup video ho tro `9:16` va `16:9`, dong bang X/overlay/Escape, pause/reset source khi dong.
- Header Landing bo `Lien he` trong menu top, them `Quy trinh`, bo login Dashboard tren header.
- Hero da cap nhat concept `TodoX AI Automation` va mobile visual `CHUYEN GIA AI / Xay kenh trieu view... / 100+ du an / 20+ nganh`.
- Founder copy da cap nhat theo noi dung `Hon 6 nam kinh nghiem... chuyen gia affiliate... san xuat video ngan...`.
- Contact card da chinh nen sang hon, gold radial glow, gold border va input tach khoi panel ro hon.
- Footer giu Dashboard login trong nhom `Giai phap`.
- Contact phone cap nhat `0366 699 961`, email `hello@todox.vn`, copyright `© 2026 TodoX.`
- Founder CSS tro ve `/img/landing/tran-trong-tuyen.png`, khong con placeholder mockup.
- `TodoX.Web` them Dashboard route `/landing/industries`.
- Dashboard co list/create/update/toggle active/soft delete/restore/reorder/preview video.
- Dashboard upload thumbnail/video qua shared media `/media`, validate extension/content-type/size, safe random filename, temp write va atomic move.
- Dashboard `Xem thu video` da chuyen sang MudBlazor dialog preview.
- Them typed options `SharedMedia` cho ca `TodoX.Landing` va `TodoX.Web`.
- Ca hai app map shared physical root sang request path `/media`.
- Landing `/health/ready` kiem tra contact DB, industry DB va shared media.
- Tao SQL thu cong `database/manual/todox_landing_industry_solutions.sql`.

## Chua thuc hien co chu dich

- Khong chay SQL.
- Khong tao/chay EF migration.
- Khong deploy/restart production.
- Khong commit.

## Can lam tren server

1. Owner/DBA review va chay thu cong `database/manual/todox_landing_industry_solutions.sql`.
2. Provision shared folder, vi du `D:\TodoXData\shared-media`.
3. Cap quyen IIS:
   - `TodoX.Web` App Pool: Modify / Read / Write
   - `TodoX.Landing` App Pool: Read
4. Dam bao anh founder ton tai tai `TodoX.Landing/wwwroot/img/landing/tran-trong-tuyen.png`.
5. Override env vars `SharedMedia__...` neu production dung duong dan khac.

## Validation

Da chay va thanh cong:

```text
node --check TodoX.Landing\wwwroot\js\landing.js
dotnet build TodoX.Landing\TodoX.Landing.csproj -c Release --no-restore
dotnet build TodoX.Web\TodoX.Web.csproj -c Release --no-restore
dotnet test TodoX.Landing\TodoX.Landing.csproj --no-restore
dotnet test TodoX.Web\TodoX.Web.csproj --no-restore
git diff --check
dotnet publish TodoX.Landing\TodoX.Landing.csproj -c Release -o artifacts\publish\todox-landing
dotnet publish TodoX.Web\TodoX.Web.csproj -c Release -o artifacts\publish\todox-dashboard
```

Publish output:

```text
artifacts\publish\todox-landing
artifacts\publish\todox-dashboard
```

`git diff --check` chi co warning LF/CRLF, khong co whitespace error.

## Browser QA

Chua hoan tat trong moi truong local nay vi khong co browser runtime kha dung va repo khong co package `playwright`.

Da kiem tra bang code/static:

- Menu top khong con `Lien he`, co `Quy trinh`.
- Header khong con login Dashboard.
- Founder CSS khong con `chatstaff-consultant.jpg` hoac placeholder.
- Landing HTML co `TodoX AI Automation`, `CHUYEN GIA AI`, `100+ du an`, `20+ nganh`, `Hon 6 nam...`.
- CSS mobile founder metrics dung `repeat(2, minmax(0,1fr))`.

Can chay browser screenshot QA tren may co browser/Playwright tai cac viewport: 375, 430, 768, 1024, 1366, 1920.
