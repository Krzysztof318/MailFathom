// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Commits one owner's record into the single <c>settings_accounts</c> row that holds it.</summary>
/// <remarks>
/// <para>
/// One statement is the whole of the write, which is what makes it atomic without a transaction block around it:
/// PostgreSQL runs a bare statement in a transaction of its own, so the document, the version, the update instant, and
/// the marker move together or not at all. The owner and the version in the <c>WHERE</c> clause are what make two
/// writers safe — the loser matches no row, commits nothing, and is told so by an absent result rather than by an
/// exception.
/// </para>
/// <para>
/// It is a bare command over the data source rather than an EF Core write, beside the reader and for the reason the
/// reader gives: the row is a document rather than a graph, nothing about it is tracked, and one statement decides the
/// outcome whichever side of the process asked. The owner is a parameter and no identifier is composed from anything
/// a caller supplied.
/// </para>
/// <para>
/// The marker is set unconditionally rather than only where it was false, because the statement has to leave the same
/// row whichever it met and a conditional would make the write's meaning depend on how many times it had already run.
/// </para>
/// <para>
/// What is under the integration marker is the statement and nothing else. The rules a candidate is refused by need no
/// server to decide, so they sit in <see cref="OwnerSettingsCommitRules" /> where the unit suite's measurement reaches
/// them.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this writer.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedOwnerSettingsDocumentWriter(
    NpgsqlDataSource dataSource,
    DatabaseCommandTimeout commandTimeout,
    TimeProvider timeProvider)
    : IOwnerSettingsDocumentWriter
{
    /// <summary>Replaces the record only where the row still stands at the version the candidate was composed over.</summary>
    /// <remarks>
    /// The cast is what turns the parameter into the column's own type, so the server parses the document and refuses
    /// text that is not JSON at all rather than storing something nothing can read back. <c>RETURNING</c> is what
    /// distinguishes the two outcomes in one round trip: a version when the row was replaced, nothing at all when the
    /// owner is not held or another writer had already moved their record.
    /// </remarks>
    private const string CommitRecord =
        """
        UPDATE settings_accounts
        SET "Document" = @document::jsonb,
            "Version" = "Version" + 1,
            "UpdatedAt" = @updatedAt,
            "DocumentWrittenAtRuntime" = TRUE
        WHERE "Id" = @owner AND "Version" = @expectedVersion
        RETURNING "Version";
        """;

    /// <inheritdoc />
    public async Task<long?> CommitAsync(
        MailOwnerId owner,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An owner record is written for a named owner.", nameof(owner));
        }

        OwnerSettingsCommitRules.RefuseWhatCannotBeCommitted(json, expectedVersion);

        // Opened in a step of its own rather than left to the command, because the driver reports a connect timeout
        // and a command timeout as the same shape and the two are opposite answers: one is a database that could not
        // be reached and a row that certainly stood still, the other a statement the server accepted and never
        // answered. Only this side knows which stage failed, so each stage reports what its own failure can mean.
        NpgsqlConnection connection;

        try
        {
            connection = await dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new OwnerSettingsUnwritableException(
                OwnerSettingsWriteFailures.DiagnoseWhileConnecting(exception),
                exception);
        }

        await using (connection)
        {
            try
            {
                await using var command = new NpgsqlCommand(CommitRecord, connection);

                // Written onto the command rather than inherited from the pool, because nothing puts the configured
                // bound onto this data source: the setting reaches EF Core's commands through the enrichment that
                // configures its context, and this statement is not one of those.
                command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;

                command.Parameters.AddWithValue("document", json);
                command.Parameters.AddWithValue("updatedAt", timeProvider.GetUtcNow());
                command.Parameters.AddWithValue("owner", owner.Value);
                command.Parameters.AddWithValue("expectedVersion", expectedVersion);

                return await command.ExecuteScalarAsync(cancellationToken) as long?;
            }
            catch (NpgsqlException exception)
            {
                throw new OwnerSettingsUnwritableException(OwnerSettingsWriteFailures.Diagnose(exception), exception);
            }
        }
    }
}
