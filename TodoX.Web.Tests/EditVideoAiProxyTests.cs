using System.Text.Json;
using System.Text.Json.Nodes;
using TodoX.Web.Services.EditVideoTool;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class EditVideoAiProxyTests
{
    private static JsonObject Model(string json) => JsonNode.Parse(json)!.AsObject();

    private static readonly JsonObject Banana = Model("""
        {"model":"banana","status":"ON","price":500,
         "ratios":[{"name":"9:16","type":"9:16"},{"name":"16:9","type":"16:9"}],
         "modes":[{"name":"VIP","type":"vip"}],
         "resolutions":[{"name":"1k","type":"1k"},{"name":"2k","type":"2k"}],
         "prices":[{"mode":"vip","resolution":"1k","price":400},{"mode":"vip","resolution":"2k","price":800}]}
        """);

    private static readonly JsonObject Veo = Model("""
        {"model":"veo","status":"ON","price":2000,"startImage":true,"withReference":false,
         "ratios":[{"type":"9:16"}],"modes":[{"type":"flash"},{"type":"pro"}],"resolutions":[{"type":"720p"}],
         "durations":[{"name":"8s","type":"8"},{"name":"10s","type":"10"}],
         "prices":[{"mode":"flash","resolution":"720p","duration":"8","price":2700},{"mode":"flash","resolution":"720p","duration":"10","price":3200},
                   {"mode":"pro","resolution":"720p","duration":"8","price":6000}]}
        """);

    [Fact]
    public void Quote_prices_the_chosen_combination_and_fills_single_option_fields()
    {
        var quote = EditVideoGommoJson.Quote(Banana, new EditVideoAiSelection("9:16", "2k", null, null));

        Assert.True(quote.Ok);
        Assert.Equal(800, quote.Credits);
        Assert.Equal("vip", quote.Selection.Mode);
    }

    [Fact]
    public void Quote_asks_instead_of_guessing_when_an_option_has_several_values()
    {
        var quote = EditVideoGommoJson.Quote(Veo, new EditVideoAiSelection(null, null, null, "8"));

        Assert.False(quote.Ok);
        Assert.Equal("mode", quote.Field);
        Assert.Equal(["flash", "pro"], quote.Choices);
    }

    [Fact]
    public void Quote_rejects_values_the_model_does_not_offer_and_unknown_combinations()
    {
        var badRatio = EditVideoGommoJson.Quote(Banana, new EditVideoAiSelection("4:5", "1k", "vip", null));
        Assert.Equal("ratio", badRatio.Field);
        Assert.Contains("9:16", badRatio.Choices!);

        var combo = EditVideoGommoJson.Quote(Veo, new EditVideoAiSelection("9:16", "720p", "pro", "10"));
        Assert.Equal("combination", combo.Field);
    }

    [Fact]
    public void Quote_for_video_fills_the_only_ratio_and_resolution()
    {
        var quote = EditVideoGommoJson.Quote(Veo, new EditVideoAiSelection(null, null, "flash", "10"));

        Assert.True(quote.Ok);
        Assert.Equal(3200, quote.Credits);
        Assert.Equal("720p", quote.Selection.Resolution);
        Assert.Equal("9:16", quote.Selection.Ratio);
    }

    [Fact]
    public void Quote_falls_back_to_the_flat_model_price()
    {
        var quote = EditVideoGommoJson.Quote(Model("""{"model":"old","price":150}"""), new EditVideoAiSelection(null, null, null, null));

        Assert.True(quote.Ok);
        Assert.Equal(150, quote.Credits);
    }

    [Theory]
    [InlineData(500, 1, 1, 10000, 0.1)]
    [InlineData(3200, 1, 1, 10000, 0.4)]
    [InlineData(3200, 1, 1.5, 10000, 0.5)]
    [InlineData(20000, 1, 1, 10000, 2.0)]
    [InlineData(0, 1, 1, 10000, 0)]
    public void Credits_convert_to_points_rounded_up_to_a_tenth(decimal credits, decimal vndPerCredit, decimal markup, decimal vndPerPoint, decimal expected) =>
        Assert.Equal(expected, EditVideoGommoJson.Points(credits, vndPerCredit, markup, vndPerPoint));

    [Fact]
    public void Model_shape_carries_options_and_point_prices_only()
    {
        var shaped = JsonSerializer.SerializeToNode(EditVideoGommoJson.Shape(Veo, "video", c => c / 1000, withPrices: true))!;

        Assert.Equal("veo", (string?)shaped["model"]);
        Assert.Equal(2.7m, (decimal)shaped["pointsFrom"]!);
        Assert.Equal(6m, (decimal)shaped["pointsTo"]!);
        Assert.Contains("startImage", shaped["abilities"]!.AsArray().Select(a => (string?)a));
        Assert.DoesNotContain("withReference", shaped["abilities"]!.AsArray().Select(a => (string?)a));
        Assert.Equal(3, shaped["prices"]!.AsArray().Count);
        Assert.Null(shaped["prices"]![0]!["price"]);
    }

    [Fact]
    public void Task_is_found_in_a_list_and_its_video_output_is_read_without_account_data()
    {
        var list = JsonNode.Parse("""
            {"videos":{"data":[
              {"id_base":"other","status":"PROCESSING"},
              {"id_base":"d828","status":"MEDIA_GENERATION_STATUS_SUCCESSFUL",
               "resolutions":[{"type":"1080p","url":"https://cdn.example/v/d828.mp4"}],
               "download_url":"https://cdn.example/v/d828.mp4",
               "author":{"name":"Someone","id_base":"fe62"}}]}}
            """);

        var task = EditVideoGommoJson.FindTask(list, "d828")!;

        Assert.Equal("succeeded", EditVideoGommoJson.Status(task));
        Assert.Equal(["https://cdn.example/v/d828.mp4"], EditVideoGommoJson.Outputs(task, "video"));
        Assert.Equal("running", EditVideoGommoJson.Status(EditVideoGommoJson.FindTask(list, "other")!));
        Assert.Null(EditVideoGommoJson.FindTask(list, "missing"));
    }

    [Fact]
    public void Image_status_and_create_ids_are_read_from_imageInfo()
    {
        var created = JsonNode.Parse("""{"imageInfo":{"id_base":"7581","status":"PENDING_ACTIVE"}}""");
        var done = JsonNode.Parse("""{"imageInfo":{"id_base":"7581","status":"SUCCESS","url":"https://cdn.example/i/7581.png?full=1","url_preview":"https://cdn.example/i/7581.png"}}""");

        Assert.Equal("7581", EditVideoGommoJson.IdBase(created));
        var task = EditVideoGommoJson.FindTask(done, "7581")!;
        Assert.Equal("succeeded", EditVideoGommoJson.Status(task));
        Assert.Equal(["https://cdn.example/i/7581.png?full=1"], EditVideoGommoJson.Outputs(task, "image"));
        Assert.Equal("failed", EditVideoGommoJson.Status(JsonNode.Parse("""{"status":"ERROR"}""")!.AsObject()));
    }

    [Fact]
    public void Rejected_create_has_no_task_id_and_keeps_gommos_reason()
    {
        // Real Gommo reply (HTTP 200) when a model needs an input video.
        var rejected = JsonNode.Parse("""{"countTasks":0,"domain":"79ai.net","success":true,"error":2100,"message":"Vui lòng đính kèm video.","runtime":0.5}""");

        Assert.Null(EditVideoGommoJson.IdBase(rejected));
        Assert.Equal("Vui lòng đính kèm video.", EditVideoGommoJson.ErrorMessage(rejected));
    }

    [Fact]
    public void Create_fields_map_inputs_to_gommo_and_never_carry_secrets()
    {
        var image = EditVideoAiService.CreateFields("image", "banana",
            new EditVideoAiCreateRequest { Prompt = " a cat ", Images = ["https://x/a.png"], References = ["https://x/b.png"] },
            new EditVideoAiSelection("9:16", "2k", "vip", null));

        Assert.Equal("a cat", image["prompt"]);
        Assert.Equal("true", image["editImage"]);
        Assert.Equal("""[{"url":"https://x/a.png"},{"url":"https://x/b.png"}]""", image["subjects"]);
        Assert.Equal("PRIVATE", image["privacy"]);
        Assert.False(image.ContainsKey("access_token"));

        var video = EditVideoAiService.CreateFields("video", "veo",
            new EditVideoAiCreateRequest { Prompt = "move", Images = ["https://x/start.png"], VideoUrls = ["https://x/src.mp4"] },
            new EditVideoAiSelection("9:16", "720p", "flash", "8"));

        Assert.Equal("8", video["duration"]);
        Assert.Equal("""[{"url":"https://x/start.png"}]""", video["images"]);
        Assert.Equal("https://x/src.mp4", video["video_url"]);
        Assert.False(video.ContainsKey("references"));
    }

    [Theory]
    [InlineData("image", null, "p", "model")]
    [InlineData("image", "m", "", "prompt")]
    [InlineData("image", "m", "p", "https", "http://insecure/a.png")]
    [InlineData("image", "m", "p", "https", "C:\\photos\\a.png")]
    [InlineData("video", "m", null, null)]
    public void Create_requests_are_validated(string kind, string? model, string? prompt, string? expectedFragment, string? image = null)
    {
        var error = EditVideoAiService.Validate(kind, new EditVideoAiCreateRequest { Model = model, Prompt = prompt, Images = image is null ? null : [image] });

        if (expectedFragment is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
            Assert.Contains(expectedFragment, error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("ai_option_required", 400)]
    [InlineData("model_not_found", 404)]
    [InlineData("insufficient_points", 402)]
    [InlineData("ai_service_unavailable", 503)]
    [InlineData("ai_failed", 502)]
    public void Ai_error_codes_map_to_http_statuses(string code, int status) =>
        Assert.Equal(status, EditVideoEndpoints.StatusFor(code));
}
