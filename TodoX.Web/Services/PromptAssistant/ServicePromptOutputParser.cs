using System.Text.Json;

namespace TodoX.Web.Services.PromptAssistant;

public sealed class ServicePromptOutputParser
{
    public JsonDocument Parse(string content)
    {
        var text = (content ?? string.Empty).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && closingFence > firstNewline)
            {
                text = text[(firstNewline + 1)..closingFence].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ServicePromptProviderException("generated_json_invalid", "Gommo Agent returned empty content.", "{}");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ServicePromptProviderException("generated_json_invalid", "Gommo Agent returned invalid JSON.", "{}",
                ex);
        }
    }
}
