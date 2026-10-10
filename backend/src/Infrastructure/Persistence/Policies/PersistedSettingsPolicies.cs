// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Reads and commits settings policies in PostgreSQL.</summary>
/// <remarks>
/// Every write is one statement, and that statement is the whole of the coordination: a first policy is an insert the
/// scope's unique index admits once, and a later one is an update conditional on the version it replaces. Two
/// replicas reaching one scope therefore need nothing between them — whichever statement lands second matches no row.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedSettingsPolicies(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : ISettingsPolicyStore
{
    /// <inheritdoc />
    public async Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        // Asked first rather than inferred from the absence of a row, because an organization that states nothing
        // holds no row either, and the two are opposite answers to whoever asked.
        if (organizationId is { } organization
            && !await dbContext.Organizations.AnyAsync(held => held.Id == organization, cancellationToken))
        {
            return null;
        }

        var stored = await dbContext.SettingsPolicies
            .AsNoTracking()
            .Where(policy => policy.OrganizationId == organizationId)
            .Select(policy => new { policy.Document, policy.Version })
            .FirstOrDefaultAsync(cancellationToken);

        return stored is null
            ? SettingsPolicyDocument.Unwritten(organizationId)
            : new SettingsPolicyDocument(organizationId, stored.Document, stored.Version);
    }

    /// <inheritdoc />
    public async Task<long?> CommitAsync(
        Guid? organizationId,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RefuseWhatCannotBeCommitted(json, expectedVersion);

        var now = timeProvider.GetUtcNow();

        try
        {
            var written = expectedVersion == SettingsPolicyDocument.UnwrittenVersion
                ? await this.InsertFirstAsync(organizationId, json, now, cancellationToken)
                : await dbContext.SettingsPolicies
                    .Where(policy => policy.OrganizationId == organizationId && policy.Version == expectedVersion)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(policy => policy.Document, json)
                            .SetProperty(policy => policy.Version, policy => policy.Version + 1)
                            .SetProperty(policy => policy.UpdatedAt, now),
                        cancellationToken);

            return written == 1 ? expectedVersion + 1 : null;
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The organization was removed between the caller's reading of its policy and this statement. Reported as
            // a write that did not land rather than raised, because the caller re-reads either way and that read is
            // what says there is no longer an organization to write for.
            return null;
        }
    }

    /// <summary>Stores a scope's first policy, unless another write stored one first.</summary>
    /// <remarks>
    /// An insert that does nothing on conflict rather than a read followed by an insert: each side of that read would
    /// see no row, and the second to commit would violate the scope's index as a provider failure instead of being
    /// told its version was superseded.
    /// </remarks>
    private Task<int> InsertFirstAsync(
        Guid? organizationId,
        string json,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);

        return dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO settings_policies ("Id", "OrganizationId", "Document", "Version", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {organizationId}, {json}::jsonb, 1, {now}, {now})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);
    }

    /// <summary>Refuses a candidate the column would store and the next read would not hand back.</summary>
    private static void RefuseWhatCannotBeCommitted(string json, long expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        int persistedOctets;

        try
        {
            if (!RootSettingsCommitRules.RootIsAnObject(json))
            {
                throw new ArgumentException(
                    "The candidate settings policy is JSON whose root is not an object, so it states nothing a policy can state.",
                    nameof(json));
            }

            persistedOctets = RootSettingsCommitRules.PersistedOctetsOf(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The candidate settings policy is not JSON, so no statement is issued for it.",
                nameof(json),
                exception);
        }

        if (persistedOctets > SettingsPolicyDocument.MaximumOctets)
        {
            throw new ArgumentException(
                $"The candidate settings policy occupies {persistedOctets} octets as the database stores it, past the {SettingsPolicyDocument.MaximumOctets} this build reads a policy from, so persisting it would leave a row nobody could open again.",
                nameof(json));
        }
    }
}
