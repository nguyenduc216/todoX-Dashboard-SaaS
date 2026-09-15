using Microsoft.Extensions.Configuration;

namespace TodoX.Web.Services.DanceSell;

public sealed class RDanceFeatureService : IRDanceFeatureService
{
    private readonly IConfiguration _configuration;

    public RDanceFeatureService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsNewUiEnabled
        => _configuration.GetValue<bool?>("Features:EnableRDanceNewUI")
           ?? _configuration.GetValue<bool>("Features:RdnOnePageUiEnabled");
}
