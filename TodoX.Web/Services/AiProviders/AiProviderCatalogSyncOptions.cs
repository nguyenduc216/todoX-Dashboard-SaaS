namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderCatalogSyncOptions
{
    public const string SectionName = "AiProviderCatalogSync";

    public bool Enabled { get; set; }
    public int DailyHourLocal { get; set; } = 2;
    public string[] ProviderCodes { get; set; } = ["79ai"];
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryDelaySeconds { get; set; } = 30;
}
