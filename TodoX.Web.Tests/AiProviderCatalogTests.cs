using TodoX.Web.Models;
using Xunit;

namespace TodoX.Web.Tests;

public class AiProviderCatalogTests
{
    [Fact]
    public void CapabilityCatalog_IncludesSceneImageGeneration()
    {
        Assert.Contains("scene_image_generation", AiProviderCatalog.CapabilityCodes);
    }
}
