namespace TodoX.Web.Models.Catalog;

public sealed record CommercialVideoServiceSeed(
    string ServiceCode,
    string ServiceName,
    string ShortDescription,
    string Description,
    string EngineType,
    int SortOrder,
    string ThumbnailManifestKey);

public static class CommercialVideoServiceCatalog
{
    public static IReadOnlyList<CommercialVideoServiceSeed> Services { get; } =
    [
        new(
            "CONSTRUCTION_VIDEO",
            "Xây dựng & Công trình",
            "Biến hình ảnh công trình thành video AI chuyên nghiệp, thể hiện quy trình thi công, tiến độ, năng lực đội ngũ và giá trị dự án một cách trực quan.",
            "Dịch vụ phù hợp cho các đơn vị thi công, xây dựng, nhà thầu và đơn vị thiết kế muốn tạo video giới thiệu công trình nhanh chóng bằng AI. Từ hình ảnh thực tế hoặc hình hoàn thiện, hệ thống có thể hỗ trợ tạo video quy trình, video quảng bá dự án, video showcase năng lực thi công và nội dung truyền thông cho thương hiệu ngành xây dựng.",
            TodoXServiceEngineTypes.Timelapse,
            10,
            "nganh-xay-dung"),
        new(
            "BUDDHISM_CONTENT_VIDEO",
            "Phật pháp & Nội dung tu học",
            "Tạo video AI cho nội dung Phật pháp, bài giảng, tu học và lan tỏa giá trị từ bi, trí tuệ một cách gần gũi và truyền cảm.",
            "Dịch vụ dành cho các kênh Phật pháp, tổ chức tu học, truyền thông tâm linh hoặc cộng đồng muốn sản xuất video mang giá trị an lạc, giáo dục và tỉnh thức. Nội dung có thể hướng tới kể chuyện, trích dẫn, chia sẻ giáo lý, video hoạt họa hoặc video cảm hứng để tăng khả năng tiếp cận trên các nền tảng số.",
            TodoXServiceEngineTypes.RVideo,
            20,
            "nganh-phat-phap"),
        new(
            "HEALTHCARE_VIDEO",
            "Sức khỏe",
            "Xây dựng video AI cho sản phẩm và dịch vụ sức khỏe, giúp truyền tải thông tin dễ hiểu, hấp dẫn và tăng niềm tin khách hàng.",
            "Phù hợp với sản phẩm chăm sóc sức khỏe, giáo dục sức khỏe, nhà thuốc, phòng khám hoặc nội dung truyền thông cộng đồng. Hệ thống giúp biến kiến thức chuyên môn thành video trực quan, dễ xem, dễ nhớ; từ đó nâng cao hiệu quả truyền thông, tăng khả năng giữ chân người xem và thúc đẩy chuyển đổi.",
            TodoXServiceEngineTypes.RVideo,
            30,
            "nganh-suc-khoe"),
        new(
            "COSMETICS_VIDEO",
            "Mỹ phẩm",
            "Tạo video mỹ phẩm cuốn hút theo phong cách review, giới thiệu sản phẩm, beauty content và social commerce.",
            "Dịch vụ phù hợp cho thương hiệu mỹ phẩm, spa, cửa hàng beauty và người bán hàng online muốn tạo video bắt mắt, hiện đại và có tính chuyển đổi cao. Nội dung có thể tập trung vào trải nghiệm sản phẩm, demo công dụng, cảm nhận người dùng, video ngắn viral hoặc video bán hàng tối ưu cho TikTok, Reels và Facebook.",
            TodoXServiceEngineTypes.RVideo,
            40,
            "nganh-my-pham"),
        new(
            "FASHION_VIDEO",
            "Thời trang",
            "Biến hình ảnh sản phẩm thời trang thành video AI sinh động, phù hợp cho lookbook, bán hàng và quảng bá thương hiệu.",
            "Dành cho shop thời trang, thương hiệu quần áo, xưởng may và nhà bán hàng muốn tạo nội dung giới thiệu sản phẩm nổi bật hơn. Có thể triển khai video lookbook, mix & match, catwalk, review outfit, nội dung mùa vụ hoặc các video ngắn phục vụ bán hàng đa nền tảng.",
            TodoXServiceEngineTypes.RDance,
            50,
            "nganh-thoi-trang"),
        new(
            "FOOD_SNACK_VIDEO",
            "Ẩm thực & Đồ ăn vặt",
            "Tạo video AI hấp dẫn cho món ăn, đồ uống và đồ ăn vặt, giúp nội dung bắt mắt hơn và tăng khả năng thu hút khách hàng.",
            "Phù hợp với nhà hàng, quán ăn, thương hiệu đồ uống, đồ ăn vặt hoặc người bán hàng online muốn tạo video ngon mắt, kích thích người xem. Nội dung có thể là giới thiệu món, quay dựng sản phẩm, menu nổi bật, combo ưu đãi, video bắt trend hoặc video social commerce phục vụ bán hàng.",
            TodoXServiceEngineTypes.RVideo,
            60,
            "nganh-am-thuc-do-an-vat"),
        new(
            "ETHICAL_KNOWLEDGE_VIDEO",
            "Video kiến thức đạo lý",
            "Sản xuất video AI truyền cảm hứng về đạo lý sống, tri thức, tư duy tích cực và các giá trị nhân văn.",
            "Dịch vụ hướng tới các kênh chia sẻ tri thức, phát triển bản thân, giáo dục giá trị sống và truyền thông định hướng tích cực. Nội dung có thể là bài học ngắn, video trích dẫn, kể chuyện, tư duy sống đẹp, truyền cảm hứng hoặc xây dựng kênh nội dung giáo dục giá trị bền vững.",
            TodoXServiceEngineTypes.RVideo,
            70,
            "video-kien-thuc-dao-ly"),
        new(
            "REAL_ESTATE_VIDEO",
            "Bất động sản",
            "Tạo video AI cho nhà đất, dự án và sản phẩm bất động sản, giúp hình ảnh chuyên nghiệp hơn và tăng hiệu quả tiếp cận khách hàng.",
            "Dành cho môi giới, sàn giao dịch, chủ đầu tư hoặc đội nhóm truyền thông bất động sản muốn tạo video giới thiệu dự án, nhà mẫu, tiện ích, quy hoạch, lifestyle và nội dung bán hàng. Hệ thống giúp nội dung rõ ràng, sinh động và thuận tiện khi triển khai trên nhiều nền tảng.",
            TodoXServiceEngineTypes.RVideo,
            80,
            "nganh-bat-dong-san"),
        new(
            "LIVESTREAM_MODEL_VIDEO",
            "Livestream - Người mẫu",
            "Hỗ trợ tạo video AI cho livestream bán hàng, review sản phẩm và hình thức nội dung có người mẫu nhằm tăng tương tác và chuyển đổi.",
            "Dịch vụ phù hợp cho các lĩnh vực cần nhân vật đại diện, livestreamer, người mẫu hoặc creator để tăng tính cảm xúc và tỷ lệ chốt đơn. Nội dung có thể là demo sản phẩm, review, kịch bản livestream, video pre-live, video cut từ livestream và nội dung social commerce thiên về bán hàng.",
            TodoXServiceEngineTypes.RDance,
            90,
            "nganh-livestream-nguoi-mau"),
        new(
            "PERSONAL_BRAND_CHANNEL_VIDEO",
            "Xây kênh nhãn hiệu",
            "Xây dựng hệ thống video AI phục vụ phát triển thương hiệu cá nhân, thương hiệu doanh nghiệp và kênh nội dung dài hạn.",
            "Dịch vụ dành cho cá nhân, chuyên gia, doanh nghiệp và creator muốn xây dựng kênh nội dung có định hướng rõ ràng. Hệ thống giúp tạo video đều đặn, nhất quán về hình ảnh và thông điệp, từ đó tăng độ nhận diện, gây dựng uy tín và mở rộng tệp khách hàng bền vững trên nhiều nền tảng.",
            TodoXServiceEngineTypes.RVideo,
            100,
            "xay-kenh-nhan-hieu")
    ];

    public static readonly IReadOnlyList<ServiceSellPriceTemplate> BootstrapSellPrices =
    [
        new(ServiceSellPriceAssetTypes.Image, ServiceSellPriceQualityTiers.Standard, null, 3, "3 điểm / hình", 10),
        new(ServiceSellPriceAssetTypes.Image, ServiceSellPriceQualityTiers.Premium, null, 5, "5 điểm / hình", 20),
        new(ServiceSellPriceAssetTypes.VideoScene, ServiceSellPriceQualityTiers.Standard, 4, 8, "8 điểm / scene 4 giây", 30),
        new(ServiceSellPriceAssetTypes.VideoScene, ServiceSellPriceQualityTiers.Standard, 6, 10, "10 điểm / scene 6 giây", 40),
        new(ServiceSellPriceAssetTypes.VideoScene, ServiceSellPriceQualityTiers.Standard, 8, 12, "12 điểm / scene 8 giây", 50),
        new(ServiceSellPriceAssetTypes.VideoScene, ServiceSellPriceQualityTiers.Premium, 4, 12, "12 điểm / scene 4 giây", 60),
        new(ServiceSellPriceAssetTypes.VideoScene, ServiceSellPriceQualityTiers.Premium, 6, 15, "15 điểm / scene 6 giây", 70),
        new(ServiceSellPriceAssetTypes.VideoScene, ServiceSellPriceQualityTiers.Premium, 8, 18, "18 điểm / scene 8 giây", 80)
    ];
}

public sealed record ServiceSellPriceTemplate(
    string AssetType,
    string QualityTier,
    int? DurationSeconds,
    decimal SellPoints,
    string DisplayLabel,
    int SortOrder);
