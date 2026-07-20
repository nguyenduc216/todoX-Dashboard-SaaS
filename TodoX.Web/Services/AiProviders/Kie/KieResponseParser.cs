using System.Text.Json;

namespace TodoX.Web.Services.AiProviders.Kie;

public static class KieResponseParser
{
    public static KieCreateTaskResult ParseCreateTask(string rawResponse, int httpStatus)
    {
        var envelope = JsonSerializer.Deserialize<KieEnvelope<KieCreateTaskData>>(rawResponse, KieJson.Options)
                       ?? new KieEnvelope<KieCreateTaskData>();
        return new KieCreateTaskResult
        {
            TaskId = envelope.Data?.TaskId,
            HttpStatus = httpStatus,
            RawResponse = rawResponse
        };
    }

    public static KieTaskDetailResult ParseTaskDetail(string rawResponse, int httpStatus, string? fallbackTaskId = null)
    {
        var envelope = JsonSerializer.Deserialize<KieEnvelope<KieTaskDetailData>>(rawResponse, KieJson.Options)
                       ?? new KieEnvelope<KieTaskDetailData>();
        var data = envelope.Data ?? new KieTaskDetailData();
        var (urls, parseError) = ParseResultUrls(data.ResultJson);
        var mapped = KieTaskStatusMapper.Map(data.State);

        return new KieTaskDetailResult
        {
            TaskId = string.IsNullOrWhiteSpace(data.TaskId) ? fallbackTaskId : data.TaskId,
            ProviderState = data.State,
            Status = mapped,
            ResultJson = data.ResultJson,
            ResultUrls = urls,
            ResultParseError = parseError,
            FailCode = data.FailCode,
            FailMsg = data.FailMsg,
            HttpStatus = httpStatus,
            RawResponse = rawResponse
        };
    }

    public static KieCallbackResult ParseCallback(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object
            ? dataElement
            : root;
        var taskId = ReadString(data, "taskId") ?? ReadString(root, "taskId");
        var state = ReadString(data, "state") ?? ReadString(root, "state");
        var resultJson = ReadString(data, "resultJson") ?? ReadString(root, "resultJson");
        var (urls, parseError) = ParseResultUrls(resultJson);

        return new KieCallbackResult
        {
            TaskId = taskId,
            ProviderState = state,
            Status = KieTaskStatusMapper.Map(state),
            RawJson = rawJson,
            ResultJson = resultJson,
            ResultUrls = urls,
            ResultParseError = parseError,
            FailCode = ReadString(data, "failCode") ?? ReadString(root, "failCode"),
            FailMsg = ReadString(data, "failMsg") ?? ReadString(root, "failMsg")
        };
    }

    public static (IReadOnlyList<string> Urls, string? Error) ParseResultUrls(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return (Array.Empty<string>(), null);
        }

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("resultUrls", out var resultUrls) || resultUrls.ValueKind != JsonValueKind.Array)
            {
                return (Array.Empty<string>(), "resultUrls missing or not an array.");
            }

            var urls = resultUrls.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToArray();
            return (urls, null);
        }
        catch (JsonException ex)
        {
            return (Array.Empty<string>(), ex.Message);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
