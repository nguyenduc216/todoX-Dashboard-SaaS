$path = 'D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\TodoX.Web\Components\Pages\RDanceJobDetail.razor'
$text = [System.IO.File]::ReadAllText($path, [System.Text.UTF8Encoding]::new($false))

$replacements = @(
    @("TiÃªu Ä‘á» video", "Tiêu đề video"),
    @("Táº¡o video nháº£y AI tá»« video tham chiáº¿u vÃ  áº£nh tham chiáº¿u, video dá»c 9:16 phÃ¹ há»£p TikTok / Reels / Shorts.", "Tạo video nhảy AI từ video tham chiếu và ảnh tham chiếu, video dọc 9:16 phù hợp TikTok / Reels / Shorts."),
    @("Tráº¡ng thÃ¡i", "Trạng thái"),
    @("Tá»· lá»‡ khung hÃ¬nh", "Tỷ lệ khung hình"),
    @("Thá»i lÆ°á»£ng dá»± kiáº¿n", "Thời lượng dự kiến"),
    @("Äiá»ƒm dá»± kiáº¿n", "Điểm dự kiến"),
    @("BÆ°á»›c 1. Video tham chiáº¿u", "Bước 1. Video tham chiếu"),
    @("Nháº­p link TikTok hoáº·c táº£i lÃªn video tham chiáº¿u.", "Nhập link TikTok hoặc tải lên video tham chiếu."),
    @("Thá»i lÆ°á»£ng", "Thời lượng"),
    @("Tá»· lá»‡", "Tỷ lệ"),
    @("Äá»™ phÃ¢n giáº£i", "Độ phân giải"),
    @("BÆ°á»›c 2. Táº¡o áº£nh tham chiáº¿u", "Bước 2. Tạo ảnh tham chiếu"),
    @("Ba cá»™t ngang cho áº£nh ngÆ°á»i máº«u, áº£nh sáº£n pháº©m vÃ  áº£nh káº¿t quáº£.", "Ba cột ngang cho ảnh người mẫu, ảnh sản phẩm và ảnh kết quả."),
    @("áº¢nh ngÆ°á»i máº«u", "Ảnh người mẫu"),
    @("áº¢nh dá»c 9:16", "Ảnh dọc 9:16"),
    @("áº¢nh sáº£n pháº©m", "Ảnh sản phẩm"),
    @("TÃ¹y chá»n", "Tùy chọn"),
    @("áº¢nh káº¿t quáº£", "Ảnh kết quả"),
    @("BÆ°á»›c 3. Táº¡o video káº¿t quáº£", "Bước 3. Tạo video kết quả"),
    @("Kiá»ƒm tra Ä‘áº§u vÃ o, Ä‘iá»ƒm dá»± kiáº¿n vÃ  táº¡o video 9:16.", "Kiểm tra đầu vào, điểm dự kiến và tạo video 9:16."),
    @("Äiá»ƒm hiá»‡n cÃ³", "Điểm hiện có"),
    @("Äiá»ƒm cÃ²n láº¡i sau khi thá»±c hiá»‡n", "Điểm còn lại sau khi thực hiện"),
    @("Táº¡o video", "Tạo video"),
    @("TÃ¬nh tráº¡ng job", "Tình trạng job"),
    @("Xem nhanh video káº¿t quáº£", "Xem nhanh video kết quả"),
    @("Kiá»ƒm tra Ä‘áº§u vÃ o", "Kiểm tra đầu vào"),
    @("Video tham chiáº¿u", "Video tham chiếu"),
    @("Sáºµn sÃ ng táº¡o video", "Sẵn sàng tạo video")
)

foreach ($pair in $replacements) {
    $text = $text.Replace($pair[0], $pair[1])
}

[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
