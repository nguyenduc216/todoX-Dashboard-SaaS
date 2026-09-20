using System.Text.Json;

namespace TodoX.Web.Services.PromptAssistant;

public sealed class ServicePromptCompiler
{
    public string Compile(
        string serviceContext,
        string userRequest,
        string templateJson,
        string descriptionContent)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
        {
            throw new ArgumentException("User request is required.", nameof(userRequest));
        }

        using var template = JsonDocument.Parse(templateJson);
        var templateText = JsonSerializer.Serialize(template.RootElement, ServicePromptJson.Options);
        return $"""
            You are a structured prompt generator for todoX.

            The supplied JSON template is the authoritative structure.

            Rules:
            1. Preserve the JSON structure.
            2. Do not rename fields.
            3. Do not remove fields that exist in the template.
            4. Do not add undocumented fields unless the structure description explicitly permits them.
            5. Preserve data types.
            6. Preserve nesting.
            7. Follow the structure description for semantic meaning.
            8. Generate content based on the user request.
            9. Return valid JSON only.
            10. Do not wrap JSON in markdown fences.
            11. Do not add explanation before or after JSON.

            SERVICE CONTEXT:
            {serviceContext}

            JSON TEMPLATE:
            {templateText}

            STRUCTURE DESCRIPTION:
            {descriptionContent}
            """;
    }

    public string CompileRepair(
        string serviceContext,
        string userRequest,
        string templateJson,
        string descriptionContent,
        string previousOutput,
        IReadOnlyList<ServicePromptValidationError> errors)
    {
        var errorText = string.Join(
            Environment.NewLine,
            errors.Select(x => $"- {x.Path}: {x.Code}: {x.Message}"));
        return $"""
            {Compile(serviceContext, userRequest, templateJson, descriptionContent)}

            The previous generated JSON failed structural validation.
            Validation errors:
            {errorText}

            Previous generated output:
            {previousOutput}

            Return a corrected valid JSON object only.
            """;
    }
}
