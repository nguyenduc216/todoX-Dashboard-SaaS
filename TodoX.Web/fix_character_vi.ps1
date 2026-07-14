$files = @(
  'D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\Components\Pages\AiCharacterEdit.razor',
  'D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\Components\Pages\AdminAvatarManager.razor'
)

$replacements = @(
  @('T?o nhân v?t AI', 'Tạo nhân vật AI'),
  @('Nhân v?t AI', 'Nhân vật AI'),
  @('Thi?t k? nhân v?t AI nh?t quán d? dùng l?i trong video TodoX.', 'Thiết kế nhân vật AI nhất quán để dùng lại trong video TodoX.'),
  @('Quay l?i', 'Quay lại'),
  @('Ðang t?i nhân v?t...', 'Đang tải nhân vật...'),
  @('Th�ng tin nhân v?t', 'Thông tin nhân vật'),
  @('Tên nhân v?t', 'Tên nhân vật'),
  @('Mô t? nhân v?t', 'Mô tả nhân vật'),
  @('Prompt render hình ?nh', 'Prompt render hình ảnh'),
  @('Có th? d? tr?ng d? TodoX t? t?o prompt t? mô t?. Prompt này s? du?c g?i tr?c ti?p d?n provider render.', 'Có thể để trống để TodoX tự tạo prompt từ mô tả. Prompt này sẽ được gửi trực tiếp đến provider render.'),
  @('Phong c�ch', 'Phong cách'),
  @('Gi?i tính', 'Giới tính'),
  @('T? l? khung hình', 'Tỷ lệ khung hình'),
  @('Tr?ng thái', 'Trạng thái'),
  @('T?m t?t', 'Tạm tắt'),
  @('Chua c?u hình provider cho <b>character_generation</b>.', 'Chưa cấu hình provider cho <b>character_generation</b>.'),
  @('Render ?nh sau khi luu', 'Render ảnh sau khi lưu'),
  @('URL ?nh tham chi?u', 'URL ảnh tham chiếu'),
  @('M?i d�ng l� m?t URL http/https.', 'Mỗi dòng là một URL http/https.'),
  @('Thêm ảnh tham chi?u', 'Thêm ảnh tham chiếu'),
  @('Lưu nhân v?t', 'Lưu nhân vật'),
  @('L?u', 'Lưu'),
  @('Nh?t ký render', 'Nhật ký render'),
  @('Log ch?y tr?c ti?p trên trang này. T?i du?c file ngay c? khi backend l?i.', 'Log chạy trực tiếp trên trang này. Tải được file ngay cả khi backend lỗi.'),
  @('Sao chép log', 'Sao chép log'),
  @('T?i .txt', 'Tải .txt'),
  @('T?i .json', 'Tải .json'),
  @('Xóa log', 'Xóa log'),
  @('L?i g?n nh?t', 'Lỗi gần nhất'),
  @('Ch?n làm master', 'Chọn làm master'),
  @('Sao chép URL', 'Sao chép URL'),
  @('Sao chép prompt', 'Sao chép prompt'),
  @('di?m', 'điểm'),
  @('T?i h?nh ?nh...', 'Tải hình ảnh...'),
  @('T?i chân dung nhân v?t...', 'Tải chân dung nhân vật...'),
  @('Ch? du?c ch?n m?t ảnh d? làm master.', 'Chỉ được chọn một ảnh để làm master.'),
  @('Ảnh master', 'Ảnh master'),
  @('Nhân v?t này ch?a có ảnh master.', 'Nhân vật này chưa có ảnh master.'),
  @('Không render? Upload ?nh master t?i dây', 'Không render? Upload ảnh master tại đây'),
  @('Ch?n t?p', 'Chọn tệp'),
  @('Khong có tệp nào được ch?n', 'Không có tệp nào được chọn'),
  @('Qu?n lý avatar m?u', 'Quản lý avatar mẫu'),
  @('T?o avatar m?u, render th? b?ng ImageAICreativeRender và luu vào thu vi?n dùng l?i.', 'Tạo avatar mẫu, render thử bằng ImageAICreativeRender và lưu vào thư viện dùng lại.'),
  @('Làm m?i form', 'Làm mới form'),
  @('Danh sách avatar', 'Danh sách avatar'),
  @('Chua có avatar m?u.', 'Chưa có avatar mẫu.'),
  @('Th�ng tin avatar', 'Thông tin avatar'),
  @('Luu avatar', 'Lưu avatar'),
  @('Chua c?u h�nh provider render ?nh cho <b>avatar_generation</b>. Vào <b>AI Providers</b> d? b?t provider.', 'Chưa cấu hình provider render ảnh cho <b>avatar_generation</b>. Vào <b>AI Providers</b> để bật provider.'),
  @('Chua render th?. B?m Render th? d? t?o preview b?ng ImageAICreativeRender.', 'Chưa render thử. Bấm Render thử để tạo preview bằng ImageAICreativeRender.'),
  @('Chua c� log.', 'Chưa có log.'),
  @('Ðang x? lý...', 'Đang xử lý...'),
  @('Chưa có avatar m?u.', 'Chưa có avatar mẫu.'),
  @('Riêng tu', 'Riêng tư'),
  @('Ðang ho?t d?ng', 'Đang hoạt động'),
  @('Ðã ?n', 'Đã ẩn')
)

foreach ($file in $files) {
  $text = [System.IO.File]::ReadAllText($file)
  foreach ($pair in $replacements) {
    $text = $text.Replace($pair[0], $pair[1])
  }
  [System.IO.File]::WriteAllText($file, $text, [System.Text.UTF8Encoding]::new($true))
}
