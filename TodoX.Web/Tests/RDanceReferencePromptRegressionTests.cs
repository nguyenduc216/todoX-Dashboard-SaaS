using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceReferencePromptRegressionTests
{
    [Fact]
    public void Ai79ProviderUsesRequestPromptAndKeepsSingleImageContract()
    {
        var source = ReadRepoFile("Services", "DanceSell", "DanceSellAiOperations.cs");
        var submit = source[source.IndexOf("public async Task<ProviderTaskSubmitResult> SubmitAsync", StringComparison.Ordinal)..];

        Assert.Contains("var prompt = string.IsNullOrWhiteSpace(request.Prompt)", submit);
        Assert.Contains("prompt,", submit);
        Assert.Contains("num_outputs = 1", submit);
        Assert.Contains("exactly ONE final image", submit);
        Assert.Contains("Do NOT create a collage", submit);
        Assert.Contains("Do NOT create a triptych", submit);
        Assert.Contains("Do NOT create multiple panels", submit);
        Assert.Contains("Do NOT duplicate the person", submit);
        Assert.DoesNotContain("prompt = BuildReferencePrompt(hasProduct)", submit);
    }

    [Fact]
    public void DanceSellServicePrefersPersistedImagePromptAndRegeneratesVersions()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var page = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("!string.IsNullOrWhiteSpace(job.ImagePrompt)", service);
        Assert.Contains("Prompt = BuildReferencePrompt(job)", service);
        Assert.Contains("ImagePrompt = prompt", page);
        Assert.Contains("References.GenerateAsync(_job!.Id", page);
        Assert.Contains("VersionNo = versionNo", service);
        Assert.Contains("Prompt = BuildReferencePrompt(job)", service);
    }

    [Fact]
    public void ReferencePromptDialogExposesSaveAndRegenerateActions()
    {
        var dialog = ReadRepoFile("Components", "Dialogs", "RDanceReferencePromptDialog.razor");

        Assert.Contains("Lưu prompt", dialog);
        Assert.Contains("Tạo lại ảnh", dialog);
        Assert.Contains("new RDanceReferencePromptDialogResult", dialog);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
