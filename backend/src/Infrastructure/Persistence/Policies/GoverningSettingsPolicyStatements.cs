// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>The statements every writer of a governed record shares: the lock a policy write excludes them with, and the read of the policies that govern one record.</summary>
/// <remarks>
/// <para>
/// What a governed record is served, and what a stored projection of it holds, is derived from the policies in force.
/// A writer that derived either from a policy it read before a policy write committed would commit a value that
/// write had just replaced, so every such writer reads the policies inside its own transaction and under one lock:
/// shared between the writes of records, which have nothing to wait for in each other, and exclusive for a policy
/// write, which waits for the record writes in flight and holds the next ones until it has committed.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
/// § <em>A policy that would leave a record invalid is refused</em> holds the rule this upholds.
/// </para>
/// <para>
/// A transaction-level advisory lock rather than a row to contend on, for the reason <c>StoredContentClaimStore</c>
/// gives: a scope that stores no policy has no row, and its first policy is an insert no reader could have locked.
/// It is taken in a statement of its own and before any row is locked, so that a writer waiting on it holds nothing
/// a policy write needs.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class GoverningSettingsPolicyStatements
{
    /// <summary>Holds the lock as a writer of a governed record does, beside every other such writer.</summary>
    /// <remarks>The key is arbitrary, and distinct from every other advisory key this product takes.</remarks>
    internal const string HoldBesideRecordWrites = "SELECT pg_advisory_xact_lock_shared(5263456017113920037);";

    /// <summary>Holds the lock as a policy write does, against every writer of a governed record and every other policy write.</summary>
    internal const string HoldAgainstRecordWrites = "SELECT pg_advisory_xact_lock(5263456017113920037);";

    /// <summary>Reads the deployment's policy and one organization's, each only where it is within what this build reads a policy from.</summary>
    private const string SelectGoverning =
        """
        SELECT
            "OrganizationId",
            CASE WHEN octet_length("Document"::text) <= @maximumOctets THEN "Document"::text END AS "Document",
            "Version"
        FROM settings_policies
        WHERE "OrganizationId" IS NULL OR "OrganizationId" = @organization;
        """;

    /// <summary>Reads the policies that govern a record of one organization, or of none.</summary>
    /// <param name="connection">The connection the record is read or written on.</param>
    /// <param name="transaction">The transaction the read joins, or <see langword="null" /> for a read outside one.</param>
    /// <param name="organizationId">The organization the record belongs to, or <see langword="null" /> for none.</param>
    /// <param name="commandTimeoutSeconds">The bound on the statement.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The deployment's policy and, for a record in an organization, that organization's; each stating nothing where its scope stores none.</returns>
    internal static async Task<GoverningSettingsPolicies> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid? organizationId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SelectGoverning, connection, transaction);

        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.AddWithValue("maximumOctets", SettingsPolicyDocument.MaximumOctets);
        command.Parameters.Add(new NpgsqlParameter("organization", NpgsqlDbType.Uuid)
        {
            Value = organizationId is { } named ? named : DBNull.Value,
        });

        var deployment = SettingsPolicyDocument.Unwritten(organizationId: null);
        var organization = organizationId is null ? null : SettingsPolicyDocument.Unwritten(organizationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            // A document past the bound is not sent, and is handed on as text no build judges as a policy.
            var json = await reader.IsDBNullAsync(1, cancellationToken) ? string.Empty : reader.GetString(1);
            var version = reader.GetInt64(2);

            if (await reader.IsDBNullAsync(0, cancellationToken))
            {
                deployment = new SettingsPolicyDocument(OrganizationId: null, json, version);
            }
            else
            {
                organization = new SettingsPolicyDocument(reader.GetGuid(0), json, version);
            }
        }

        return new GoverningSettingsPolicies(deployment, organization);
    }
}
