using Dapper;
using Npgsql;
using TodoX.Landing.Data;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

public sealed class LandingContactRepository
{
    private readonly LandingConnectionFactory _factory;

    public LandingContactRepository(LandingConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<bool>(
                "SELECT to_regclass('landing.contact_leads') IS NOT NULL;");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or LandingSchemaUnavailableException)
        {
            return false;
        }
    }

    public async Task<string> InsertAsync(NormalizedContactLead lead, ContactLeadInsertContext context, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var command = new CommandDefinition(
                """
                INSERT INTO landing.contact_leads
                    (full_name, phone, email, company_name, industry_code, interested_product,
                     message, source_url, referrer_url, utm_source, utm_medium, utm_campaign,
                     utm_content, utm_term, ip_address, user_agent, request_id,
                     consent_accepted, consent_at, metadata_json)
                VALUES
                    (@FullName, @Phone, @Email, @Company, @Industry, @Need,
                     @Message, @SourceUrl, @ReferrerUrl, @UtmSource, @UtmMedium, @UtmCampaign,
                     @UtmContent, @UtmTerm, CAST(@IpAddress AS inet), @UserAgent, @RequestId,
                     @ConsentAccepted, now(), '{}'::jsonb)
                RETURNING lead_code;
                """,
                new
                {
                    lead.FullName,
                    lead.Phone,
                    lead.Email,
                    lead.Company,
                    lead.Industry,
                    lead.Need,
                    lead.Message,
                    lead.SourceUrl,
                    lead.ReferrerUrl,
                    lead.UtmSource,
                    lead.UtmMedium,
                    lead.UtmCampaign,
                    lead.UtmContent,
                    lead.UtmTerm,
                    context.IpAddress,
                    context.UserAgent,
                    context.RequestId,
                    lead.ConsentAccepted
                },
                cancellationToken: ct);

            return await conn.ExecuteScalarAsync<string>(command)
                ?? throw new LandingSchemaUnavailableException("Lead code was not returned.");
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "3F000" or "42883")
        {
            throw new LandingSchemaUnavailableException("Landing contact schema is not available.", ex);
        }
        catch (NpgsqlException ex)
        {
            throw new LandingSchemaUnavailableException("Landing database is not available.", ex);
        }
    }
}
