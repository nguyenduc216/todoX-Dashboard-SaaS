using System.Text.Json;

namespace TodoX.Web.Services.PromptAssistant;

public sealed class ServicePromptStructureValidator
{
    public IReadOnlyList<ServicePromptValidationError> Validate(string templateJson, string generatedJson)
    {
        using var template = JsonDocument.Parse(templateJson);
        using var generated = JsonDocument.Parse(generatedJson);
        var errors = new List<ServicePromptValidationError>();
        ValidateNode(template.RootElement, generated.RootElement, "$", errors);
        return errors;
    }

    private static void ValidateNode(
        JsonElement template,
        JsonElement generated,
        string path,
        ICollection<ServicePromptValidationError> errors)
    {
        if (template.ValueKind != generated.ValueKind
            && !(template.ValueKind == JsonValueKind.Number && generated.ValueKind == JsonValueKind.Number))
        {
            errors.Add(new(path, "wrong_type", "Generated value has a different JSON type."));
            return;
        }

        switch (template.ValueKind)
        {
            case JsonValueKind.Object:
                var templateProperties = template.EnumerateObject().ToDictionary(x => x.Name, StringComparer.Ordinal);
                var generatedProperties = generated.EnumerateObject().ToDictionary(x => x.Name, StringComparer.Ordinal);
                foreach (var property in templateProperties)
                {
                    if (!generatedProperties.TryGetValue(property.Key, out var generatedProperty))
                    {
                        errors.Add(new($"{path}.{property.Key}", "missing_field", "Required template field is missing."));
                        continue;
                    }

                    ValidateNode(property.Value.Value, generatedProperty.Value, $"{path}.{property.Key}", errors);
                }

                foreach (var property in generatedProperties.Keys.Except(templateProperties.Keys, StringComparer.Ordinal))
                {
                    errors.Add(new($"{path}.{property}", "unknown_field", "Field does not exist in template."));
                }

                break;
            case JsonValueKind.Array:
                if (generated.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                var templateItem = template.EnumerateArray().FirstOrDefault();
                if (templateItem.ValueKind == JsonValueKind.Undefined)
                {
                    return;
                }

                var index = 0;
                foreach (var item in generated.EnumerateArray())
                {
                    ValidateNode(templateItem, item, $"{path}[{index}]", errors);
                    index++;
                }

                break;
        }
    }
}
