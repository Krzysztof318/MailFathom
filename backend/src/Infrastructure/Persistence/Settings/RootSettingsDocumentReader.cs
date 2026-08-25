// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Reads the persisted configuration document from the singleton <c>settings_root</c> row.</summary>
/// <remarks>
/// <para>
/// The read is a bare command over the data source rather than an EF Core query, because the first caller is the host
/// composing its configuration: that happens before the container exists, so no <c>DbContext</c> can be resolved and
/// building one there would need the model — and the model is built from configuration this layer is part of. The
/// second caller, a reload once the process is running, uses the same command so that one statement decides what the
/// layer contains whichever moment asked.
/// </para>
/// <para>
/// The statement names the singleton key as a parameter and no identifier is composed from anything a caller supplied,
/// so there is nothing here for a value to reach.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class RootSettingsDocumentReader(NpgsqlDataSource dataSource) : IRootSettingsDocumentReader
{
    private const string SelectDocument =
        """
        SELECT "Document", "Version" FROM settings_root WHERE "Id" = @id;
        """;

    /// <summary>The PostgreSQL code for a relation that does not exist, which is how a database missing the migration answers.</summary>
    private const string UndefinedTableSqlState = "42P01";

    /// <summary>The PostgreSQL code for a rejected password, which is how a wrong or rotated credential answers.</summary>
    private const string InvalidPasswordSqlState = "28P01";

    /// <summary>The PostgreSQL code for a refused privilege, which is how a schema applied by the wrong role answers.</summary>
    private const string InsufficientPrivilegeSqlState = "42501";

    /// <inheritdoc />
    public async Task<RootSettingsDocument> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(SelectDocument);
            command.Parameters.AddWithValue("id", RootSettingsEntity.SingletonId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new RootSettingsUnreadableException(
                    "The persisted configuration row is missing from settings_root. Apply the migrations this build defines, which provision it, and start the host again.");
            }

            return new RootSettingsDocument(reader.GetString(0), reader.GetInt64(1));
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTableSqlState)
        {
            throw new RootSettingsUnreadableException(
                "The database does not carry the settings_root table this build reads its persisted configuration from. Apply the migrations this build defines and start the host again.",
                exception);
        }
        catch (PostgresException exception) when (exception.SqlState == InvalidPasswordSqlState)
        {
            throw new RootSettingsUnreadableException(
                "The database holding the persisted configuration refused the configured credential. Check the Persistence secret block rather than the network: the server answered, and what it rejected is the password MailFathom composed for it.",
                exception);
        }
        // A per-table grant on an existing deployment does not cover a table a later release adds, so this is what a
        // correctly reachable database says when the schema was applied by one role and is served by another. It runs
        // ahead of the schema gate that used to be the first to meet that condition, so it makes the same diagnosis
        // rather than leaving the operator with a database that appears unreachable.
        catch (PostgresException exception) when (exception.SqlState == InsufficientPrivilegeSqlState)
        {
            throw new RootSettingsUnreadableException(
                "The serving role holds no privilege on settings_root. Grant it on the table the persisted configuration lives in, the way the schema documentation describes for a schema applied by one role and served by another.",
                exception);
        }
        catch (NpgsqlException exception)
        {
            throw new RootSettingsUnreadableException(
                "The database holding the persisted configuration could not be reached. MailFathom composes its settings from that layer before it opens any endpoint, so it refuses to start on the sources beneath it.",
                exception);
        }
    }
}
