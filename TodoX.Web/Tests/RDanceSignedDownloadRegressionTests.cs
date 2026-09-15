using Microsoft.AspNetCore.DataProtection;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RDanceSignedDownloadRegressionTests
{
    [Fact]
    public void TicketIsBoundToJobAndDownloadType()
    {
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var ticket = service.CreateTicket(jobId, Guid.NewGuid(), Guid.NewGuid(), RDanceDownloadTypes.Result, TimeSpan.FromMinutes(3));

        var payload = service.ValidateTicket(ticket);

        Assert.Equal(jobId, payload.JobId);
        Assert.Equal(RDanceDownloadTypes.Result, payload.Type);
        Assert.Throws<InvalidOperationException>(() => service.ValidateTicket(ticket + "tampered"));
    }

    [Fact]
    public void ExpiredTicketIsRejected()
    {
        var service = CreateService();
        var ticket = service.CreateTicket(Guid.NewGuid(), null, Guid.NewGuid(), RDanceDownloadTypes.Reference, TimeSpan.FromSeconds(-1));

        Assert.Throws<InvalidOperationException>(() => service.ValidateTicket(ticket));
    }

    [Fact]
    public void RDancePageUsesSameOriginTicketDownloadInsteadOfRemoteBlobDownload()
    {
        var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Components", "Pages", "RDanceJobDetail.razor"));
        var javascript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot", "js", "todox-download.js"));

        Assert.Contains("GetDownloadTicketAsync", page);
        Assert.Contains("startBrowserDownload", page);
        Assert.DoesNotContain("downloadRemoteFile\", job.ResultVideoUrl", page);
        Assert.DoesNotContain("downloadRemoteFile\", job.PreparedReferenceUrl", page);
        var startBrowserDownload = javascript[javascript.IndexOf("startBrowserDownload:", StringComparison.Ordinal)..javascript.IndexOf("downloadRemoteFile:", StringComparison.Ordinal)];
        Assert.DoesNotContain("fetch(", startBrowserDownload);
        Assert.DoesNotContain("blob()", startBrowserDownload);
    }

    [Fact]
    public void TicketIssuanceRequiresOwnedReadyJob()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");

        Assert.Contains("var job = await RequireOwnedJobAsync(id, user, ct);", service);
        Assert.Contains("job.Status, DanceSellJobStatuses.Completed", service);
        Assert.Contains("job.ResultVideoUrl", service);
        Assert.Contains("job.PreparedReferenceStatus, DanceSellReferenceStatuses.Approved", service);
        Assert.Contains("job.PreparedReferenceUrl", service);
        Assert.Contains("TimeSpan.FromMinutes(3)", service);
    }

    [Fact]
    public void DownloadEndpointValidatesTicketAndStreamsResolvedJobUrl()
    {
        var endpoints = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Endpoints.cs");
        var executeDownload = endpoints[endpoints.IndexOf("private static async Task<IResult> ExecuteDownloadAsync", StringComparison.Ordinal)..];

        Assert.Contains("tickets.ValidateTicket(token)", executeDownload);
        Assert.Contains("ticket.JobId != id", executeDownload);
        Assert.Contains("ticket.Type, expectedType", executeDownload);
        Assert.Contains("repository.GetByIdAsync(id, ct)", executeDownload);
        Assert.Contains("job.ResultVideoUrl", executeDownload);
        Assert.Contains("job.PreparedReferenceUrl", executeDownload);
        Assert.Contains("EnsurePublicHttpsUrlAsync(remoteUri, ct)", executeDownload);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", executeDownload);
        Assert.Contains("new DanceSellRemoteDownloadResult", executeDownload);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));

    private static RDanceDownloadTicketService CreateService()
    {
        var provider = DataProtectionProvider.Create("TodoX.Tests");
        return new RDanceDownloadTicketService(provider);
    }
}
