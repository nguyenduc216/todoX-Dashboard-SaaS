using Npgsql;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public class RenderJobServiceTests
{
    [Fact]
    public void BuildProjectJobLockName_IsStableAndNormalizesJobType()
    {
        var first = RenderJobService.BuildProjectJobLockName(" Render_Scene_Images ", 123);
        var second = RenderJobService.BuildProjectJobLockName("render_scene_images", 123);

        Assert.Equal("render.render_jobs:render_scene_images:123", first);
        Assert.Equal(first, second);
        Assert.DoesNotContain("HashCode", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsRenderJobsCustomerIdNotNullViolation_MatchesOnlyExactSchemaTableColumn()
    {
        Assert.True(RenderJobService.IsRenderJobsCustomerIdNotNullViolation(
            Pg("23502", schema: "render", table: "render_jobs", column: "customer_id")));

        Assert.False(RenderJobService.IsRenderJobsCustomerIdNotNullViolation(
            Pg("23502", schema: "public", table: "render_jobs", column: "customer_id")));
        Assert.False(RenderJobService.IsRenderJobsCustomerIdNotNullViolation(
            Pg("23502", schema: "render", table: "other_jobs", column: "customer_id")));
        Assert.False(RenderJobService.IsRenderJobsCustomerIdNotNullViolation(
            Pg("23502", schema: "render", table: "render_jobs", column: "tenant_id")));
        Assert.False(RenderJobService.IsRenderJobsCustomerIdNotNullViolation(
            Pg("23503", schema: "render", table: "render_jobs", column: "customer_id")));
    }

    private static PostgresException Pg(string sqlState, string schema, string table, string column)
        => new(
            messageText: "constraint failed",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            schemaName: schema,
            tableName: table,
            columnName: column);
}
