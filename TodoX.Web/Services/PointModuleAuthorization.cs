using TodoX.Web.Models;

namespace TodoX.Web.Services;

public static class PointModulePermissions
{
    public const string PointConfigView = "point_config.view";
    public const string PointConfigManage = "point_config.manage";
    public const string WalletViewAll = "wallet.view_all";
    public const string WalletTopUp = "wallet.topup";
    public const string WalletAdjust = "wallet.adjust";
    public const string WalletRefund = "wallet.refund";
    public const string VoucherView = "voucher.view";
    public const string VoucherManage = "voucher.manage";
    public const string ServicePointOverrideManage = "service_point_override.manage";
}

public enum PointBillingIntent
{
    InitialRender,
    UserRerender,
    SystemRetry
}

public static class PointBillingReference
{
    public static Guid ForRerender(Guid jobId, string assetType, string assetId)
        => ForRerender(jobId, assetType, assetId, null);

    public static Guid ForRerender(Guid jobId, string assetType, string assetId, Guid? rerenderOperationId)
    {
        return ForOperation(jobId, assetType, assetId, PointBillingIntent.UserRerender, rerenderOperationId);
    }

    public static Guid ForOperation(
        Guid jobId,
        string assetType,
        string assetId,
        PointBillingIntent intent,
        Guid? operationId = null)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"{jobId:N}|{assetType}|{assetId}|{operationId?.ToString("N") ?? "legacy"}|{intent}"));
        return new Guid(bytes[..16]);
    }
}

public static class PointModuleAuthorization
{
    public static bool CanViewOwnWallet(CurrentUserSession? user)
        => user?.IsAuthenticated == true && user.IsCustomer && user.CustomerId.HasValue;

    public static void Require(CurrentUserSession? user, string permission)
    {
        if (user?.IsAuthenticated != true || !user.Can(permission))
        {
            throw new UnauthorizedAccessException($"Permission required: {permission}");
        }
    }

    public static void RequireOwnCustomer(CurrentUserSession? user, Guid customerId)
    {
        if (user?.IsAuthenticated != true || user.CustomerId != customerId)
        {
            throw new UnauthorizedAccessException("Customer access is required.");
        }
    }
}
