using TodoX.Web.Services.AiProviders.Kie;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class KieResponseParserTests
{
    [Fact]
    public void ParseCreateTask_ReadsTaskId()
    {
        var result = KieResponseParser.ParseCreateTask("""{"code":200,"data":{"taskId":"task-123"}}""", 200);
        Assert.Equal("task-123", result.TaskId);
    }

    [Fact]
    public void ParseTaskDetail_ReadsResultUrlsFromJsonString()
    {
        var result = KieResponseParser.ParseTaskDetail(
            """{"code":200,"data":{"taskId":"task-123","state":"success","resultJson":"{\"resultUrls\":[\"https://cdn.example.com/out.mp4\"]}"}}""",
            200);

        Assert.Equal(KieTaskStatuses.Completed, result.Status);
        Assert.Equal("https://cdn.example.com/out.mp4", Assert.Single(result.ResultUrls));
    }

    [Fact]
    public void ParseTaskDetail_ReturnsParseErrorForInvalidResultJson()
    {
        var result = KieResponseParser.ParseTaskDetail(
            """{"code":200,"data":{"taskId":"task-123","state":"success","resultJson":"not-json"}}""",
            200);

        Assert.NotNull(result.ResultParseError);
        Assert.Empty(result.ResultUrls);
    }

    [Fact]
    public void ParseTaskDetail_ReadsFailCodeAndFailMsg()
    {
        var result = KieResponseParser.ParseTaskDetail(
            """{"code":200,"data":{"taskId":"task-123","state":"fail","failCode":"BAD_INPUT","failMsg":"Invalid input."}}""",
            200);

        Assert.Equal(KieTaskStatuses.Failed, result.Status);
        Assert.Equal("BAD_INPUT", result.FailCode);
        Assert.Equal("Invalid input.", result.FailMsg);
    }
}
