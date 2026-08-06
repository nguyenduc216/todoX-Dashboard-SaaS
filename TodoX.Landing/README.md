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

## Cấu hình form tư vấn

`wwwroot/js/landing-config.js` chứa `dashboardUrl`, `contactEndpoint` và môi trường.
`contactEndpoint` hiện để trống, vì vậy form chỉ báo chế độ thử nghiệm và không giả thông báo lưu thành công.
Khi có API thật, cấu hình endpoint bằng secret/configuration của quy trình deploy, không đưa token hoặc credential vào frontend.
