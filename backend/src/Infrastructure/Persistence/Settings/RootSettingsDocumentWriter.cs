// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Commits the persisted configuration document into the singleton <c>settings_root</c> row.</summary>
/// <remarks>
/// <para>
/// One statement is the whole of the write, and that is what makes it atomic without a transaction block around it:
/// PostgreSQL runs a bare statement in a transaction of its own, so the document, the version, and the update instant
/// move together or not at all. An explicit transaction would add a round trip and a second thing to get wrong, and it
/// would not add an outcome — nothing else is written beside this row.
/// </para>
/// <para>
/// The version in the <c>WHERE</c> clause is what makes two writers safe. The loser matches no row, so it commits
/// nothing and is told so by an absent result rather than by an exception, and the winner's document is never composed
/// with an edit written against the document it replaced.
/// </para>
/// <para>
/// It is a bare command over the data source rather than an EF Core write, beside the reader and for the reason the
/// reader gives: the row is a document rather than a graph, nothing about it is tracked, and one statement decides the
/// outcome whichever side of the process asked. The singleton key is a parameter and no identifier is composed from
/// anything a caller supplied.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this writer.")]
[RequiresIntegrationCoverage]
internal sealed class RootSettingsDocumentWriter(NpgsqlDataSource dataSource, TimeProvider timeProvider)
    : IRootSettingsDocumentWriter
{
    /// <summary>Replaces the document only where the row still stands at the version the candidate was composed over.</summary>
    /// <remarks>
    /// The cast is what turns the parameter into the column's own type, so the server parses the document and refuses
    /// one that is not an object rather than storing text nothing can read back. <c>RETURNING</c> is what distinguishes
    /// the two outcomes in one round trip: a version when the row was replaced, nothing at all when another writer had
    /// already moved it.
    /// </remarks>
    private const string CommitDocument =
        """
        UPDATE settings_root
        SET "Document" = @document::jsonb,
            "Version" = "Version" + 1,
            "UpdatedAt" = @updatedAt
        WHERE "Id" = @id AND "Version" = @expectedVersion
        RETURNING "Version";
        """;

    /// <inheritdoc />
    public async Task<long?> CommitAsync(string json, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        var documentOctets = Encoding.UTF8.GetByteCount(json);

        if (documentOctets > RootSettingsDocument.MaximumOctets)
        {
            throw new ArgumentException(
                $"The candidate configuration document is {documentOctets} octets, past the {RootSettingsDocument.MaximumOctets} this build composes settings from, so persisting it would leave a row the next start refuses.",
                nameof(json));
        }

        try
        {
            await using var command = dataSource.CreateCommand(CommitDocument);
            command.Parameters.AddWithValue("document", json);
            command.Parameters.AddWithValue("updatedAt", timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("id", RootSettingsEntity.SingletonId);
            command.Parameters.AddWithValue("expectedVersion", expectedVersion);

            return await command.ExecuteScalarAsync(cancellationToken) as long?;
        }
        catch (NpgsqlException exception)
        {
            throw new RootSettingsUnwritableException(
                "The database refused the statement that would have persisted the configuration write, so the deployment's settings are unchanged. Check the serving role's privilege on settings_root and the database's availability; the write is safe to attempt again.",
                exception);
        }
    }
}
