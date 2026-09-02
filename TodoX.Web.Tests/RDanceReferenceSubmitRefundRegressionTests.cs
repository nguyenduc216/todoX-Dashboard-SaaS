using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceReferenceSubmitRefundRegressionTests
{
    [Fact]
    public void RefundTriggerOnlyCompensatesPreTaskReferenceSubmitFailure()
    {
        var sql = ReadMigration();

        Assert.Contains("NEW.operation_type <> 'reference_image'", sql);
        Assert.Contains("NEW.status <> 'failed'", sql);
        Assert.Contains("NEW.provider_task_id IS NOT NULL", sql);
        Assert.Contains("COALESCE(NEW.todox_points_charged, 0) <= 0", sql);
        Assert.Contains("COALESCE(NEW.billing_status, '') <> 'charged'", sql);
    }

    [Fact]
    public void RefundTriggerIsIdempotentByOperationReference()
    {
        var sql = ReadMigration();

        Assert.Contains("transaction_type = 'refund'", sql);
        Assert.Contains("reference_type = 'dance_sell_reference_submit_refund'", sql);
        Assert.Contains("reference_id = NEW.id", sql);
        Assert.Contains("v_already_refunded", sql);
    }

    [Fact]
    public void RefundTriggerRestoresWalletAndMarksOperationRefunded()
    {
        var sql = ReadMigration();

        Assert.Contains("v_after := v_before + v_amount", sql);
        Assert.Contains("UPDATE billing.token_wallets", sql);
        Assert.Contains("NEW.todox_points_refunded := v_amount", sql);
        Assert.Contains("NEW.refund_status := 'refunded'", sql);
        Assert.Contains("NEW.billing_status := 'refunded'", sql);
    }

    [Fact]
    public void ProviderAcceptedTaskIsExcludedFromAutomaticRefund()
    {
        var sql = ReadMigration();

        // The trigger returns without compensation whenever a provider task id exists.
        Assert.Contains("OR NEW.provider_task_id IS NOT NULL", sql);
    }

    private static string ReadMigration()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "TodoX.Web", "database", "migrations",
            "20260902_rdance_reference_submit_refund.sql"));
}
