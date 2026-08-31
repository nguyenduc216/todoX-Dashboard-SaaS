using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.Media;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiCharacterCustomerUiTests
{
    [Fact]
    public void CustomerPagesHideTechnicalProviderAndSeedFields()
    {
        var edit = ReadSource("TodoX.Web", "Components", "Pages", "AiCharacterEdit.razor");
        var list = ReadSource("TodoX.Web", "Components", "Pages", "AiCharacters.razor");

        Assert.DoesNotContain("Mã seed", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AI Provider", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProviderDisplay", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("Nhà cung cấp:", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nhà cung cấp:", list, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ModelName", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelName", list, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerUploadControlsUseIconOnlyHiddenFilePickers()
    {
        var edit = ReadSource("TodoX.Web", "Components", "Pages", "AiCharacterEdit.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "AiCharacterEdit.razor.css");

        Assert.Contains("Icons.Material.Filled.CloudUpload", edit, StringComparison.Ordinal);
        Assert.Contains("class=\"ai-character-upload-input\"", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("Chọn tệp", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Không có tệp nào được chọn", edit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clip-path: inset(50%)", css, StringComparison.Ordinal);
        Assert.Contains("title=\"Tải ảnh master\"", edit, StringComparison.Ordinal);
        Assert.Contains("EnsureCharacterExistsForUploadAsync", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled=\"@(_busy || _isNew)\"", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceUploadUsesDedicatedStorageAndReferencePersistence()
    {
        var edit = ReadSource("TodoX.Web", "Components", "Pages", "AiCharacterEdit.razor");
        var service = ReadSource("TodoX.Web", "Services", "AiCharacters", "AiCharacterService.cs");
        var repository = ReadSource("TodoX.Web", "Services", "AiCharacters", "AiCharacterRepository.cs");

        Assert.Contains("UploadReferenceImageAsync", edit, StringComparison.Ordinal);
        Assert.Contains("Characters.UploadReferenceImageAsync", edit, StringComparison.Ordinal);
        Assert.Contains("ReferenceImageUrls = _detail?.References.Select", edit, StringComparison.Ordinal);
        Assert.Contains("_media.SaveAsync", service, StringComparison.Ordinal);
        Assert.Contains("_repo.ReplaceReferencesAsync", service, StringComparison.Ordinal);
        Assert.Contains("image_url", repository, StringComparison.Ordinal);
        Assert.Contains("object_key", repository, StringComparison.Ordinal);
        Assert.Contains("reference_type", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("URL ảnh tham chiếu", edit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MasterAndReferenceUploadsRemainSeparate()
    {
        var service = ReadSource("TodoX.Web", "Services", "AiCharacters", "AiCharacterService.cs");
        var repository = ReadSource("TodoX.Web", "Services", "AiCharacters", "AiCharacterRepository.cs");

        Assert.Contains("UploadMasterImageAsync", service, StringComparison.Ordinal);
        Assert.Contains("UpdateMasterImageAsync", service, StringComparison.Ordinal);
        Assert.Contains("UploadReferenceImageAsync", service, StringComparison.Ordinal);
        Assert.Contains("ReplaceReferencesAsync", service, StringComparison.Ordinal);
        Assert.Contains("master_image_url", repository, StringComparison.Ordinal);
        Assert.Contains("todox_ai_character_reference", repository, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("photo.png", "image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })]
    [InlineData("photo.jpg", "image/jpeg", new byte[] { 255, 216, 255 })]
    [InlineData("photo.jpeg", "image/jpg", new byte[] { 255, 216, 255 })]
    [InlineData("photo.webp", "application/octet-stream", new byte[] { 82, 73, 70, 70, 0, 0, 0, 0, 87, 69, 66, 80 })]
    public void UploadValidationAcceptsSupportedImageSignatures(string fileName, string mime, byte[] signature)
    {
        var content = new byte[Math.Max(signature.Length, 16)];
        signature.CopyTo(content, 0);

        var detected = ImageUploadValidation.Validate(content, fileName, mime, 1024);

        Assert.StartsWith("image/", detected, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadValidationRejectsInvalidMimeAndOversizedFiles()
    {
        var unsupported = Assert.Throws<InvalidOperationException>(() =>
            ImageUploadValidation.Validate(new byte[] { 1, 2, 3 }, "photo.gif", "image/gif", 1024));
        Assert.Equal(ImageUploadValidation.InvalidImageMessage, unsupported.Message);

        Assert.Throws<InvalidOperationException>(() =>
            ImageUploadValidation.Validate(new byte[] { 255, 216, 255 }, "photo.jpg", "image/jpeg", 2));
    }

    [Fact]
    public void UploadValidationDoesNotTrustMimeWithoutMatchingFileSignature()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ImageUploadValidation.Validate(
                new byte[] { 0, 1, 2, 3, 4, 5 },
                "photo.jpg",
                "image/png",
                1024));
    }

    [Fact]
    public void UploadValidationRequiresMatchingExtensionAndSignature()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ImageUploadValidation.Validate(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                "photo.jpg",
                "application/octet-stream",
                1024));
    }

    [Fact]
    public void UploadValidationMessageIsUtf8AndNotMojibake()
    {
        Assert.Equal("Chỉ chấp nhận ảnh PNG, JPEG, WEBP.", ImageUploadValidation.InvalidImageMessage);
        Assert.DoesNotContain("Chá»", ImageUploadValidation.InvalidImageMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("nháº", ImageUploadValidation.InvalidImageMessage, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(GetRepositoryRoot(), Path.Combine(parts));
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TodoX.Dashboard.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
