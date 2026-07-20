using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellRepositoryTests
{
    [Fact]
    public void UpdateCompletedAsyncSql_DoesNotReferenceMissingStatusParameter()
    {
        Assert.DoesNotContain("@status", DanceSellRepository.UpdateCompletedSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateCompletedAsyncSql_PreservesExistingCompletedAt()
    {
        Assert.Contains("completed_at=COALESCE(completed_at, now())", DanceSellRepository.UpdateCompletedSql, StringComparison.OrdinalIgnoreCase);
    }
}
