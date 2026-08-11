# TodoX Landing

Landing page độc lập của TodoX, chạy bằng ASP.NET Core minimal web app trên .NET 10.

## Chạy local

Từ thư mục repository:

```powershell
dotnet run --project .\TodoX.Landing\TodoX.Landing.csproj
```

Mở `http://localhost:5172`. Health check: `http://localhost:5172/health`.

## Publish

```powershell
.\TodoX.Landing\scripts\publish-landing.ps1
```

Output mặc định:

```text
artifacts/publish/todox-landing/
```

Có thể chỉ định thư mục deploy:

```powershell
.\TodoX.Landing\scripts\publish-landing.ps1 -OutputPath "D:\Publish\todox.vn"
```

## IIS

Tạo IIS Site riêng:

- Host: `todox.vn` và tùy chọn `www.todox.vn`
- Physical path: thư mục publish của landing
- Application Pool: `TodoX-Landing`
- Binding: HTTPS với certificate cho `todox.vn`
- Không dùng chung physical path hoặc app pool với `dashboard.todox.vn`

Dashboard tiếp tục dùng Site, physical path và Application Pool riêng.
Kiểm tra sau khi binding xong tại `https://todox.vn/health`.

## Shared media cho ngành nghề

`TodoX.Web` upload thumbnail/video ngành nghề vào một thư mục dùng chung, còn `TodoX.Landing` serve thư mục đó qua `/media`.

Cấu hình production có thể override bằng biến môi trường:

```powershell
SharedMedia__StorageRoot=D:\TodoXData\shared-media
SharedMedia__RequestPath=/media
SharedMedia__IndustrySolutions__RootSubfolder=landing\industries
SharedMedia__IndustrySolutions__ThumbnailSubfolder=thumbnails
SharedMedia__IndustrySolutions__VideoSubfolder=videos
SharedMedia__IndustrySolutions__TempSubfolder=temp
```

Quyền IIS khuyến nghị:

- `TodoX.Web` App Pool: Modify / Read / Write
- `TodoX.Landing` App Pool: Read

Database chỉ lưu URL public dạng `/media/landing/industries/...`, không lưu đường dẫn vật lý Windows.

## Cấu hình form tư vấn

`wwwroot/js/landing-config.js` chứa `dashboardUrl`, `contactEndpoint` và môi trường.
`contactEndpoint` hiện để trống, vì vậy form chỉ báo chế độ thử nghiệm và không giả thông báo lưu thành công.
Khi có API thật, cấu hình endpoint bằng secret/configuration của quy trình deploy, không đưa token hoặc credential vào frontend.
