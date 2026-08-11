namespace TodoX.Web.Services.AiProviders;

public static class AiImageBillingFeatureFlags
{
    public static bool IsReconciliationWorkerEnabled(IConfiguration configuration)
    {
        if (!configuration.GetValue("AiImageBilling:ReconciliationEnabled", true))
        {
            return false;
        }

        return configuration.GetValue("AiImageBilling:HasBillingSchema", false);
    }
}
