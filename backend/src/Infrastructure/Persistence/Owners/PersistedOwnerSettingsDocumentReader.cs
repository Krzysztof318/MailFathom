// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Reads one owner's record out of the single <c>settings_accounts</c> row that holds it.</summary>
/// <remarks>
/// <para>
/// The lookup is by primary key, which is what the routing decision buys: one owner is one row, so an owner-scoped
/// view costs one seek rather than a scan of every owner's settings or a value-per-key query. Nothing here reads into
/// the document — the column travels as the text a binder will parse — because what it contains is the configuration
/// layer's to interpret and this table exists to hand that layer a row.
/// </para>
/// <para>
/// The version travels with it rather than being read again by whoever writes next. A writer that re-read the version
/// after deciding its change would be stating a number it had not composed over, which is exactly the race the version
/// exists to refuse.
/// </para>
/// <para>
/// The document is bounded before it is transferred rather than after, which is why this is a bare command over the
/// data source rather than a query composed through the model: <c>jsonb</c> holds up to a gigabyte, the bound is a
/// measurement PostgreSQL makes and the model cannot express, and a row past it read into the process is an allocation
/// failure inside a request rather than a refusal naming the limit. It is the same statement shape the deployment's
/// own document is read under, and for the same reason.
/// </para>
/// <para>
/// The statement names the owner as a parameter and composes no identifier from anything a caller supplied, so there
/// is nothing here for a value to reach.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedOwnerSettingsDocumentReader(NpgsqlDataSource dataSource) : IOwnerSettingsDocumentReader
{
    /// <summary>Reads the row, and the document with it only when the document is small enough to bind.</summary>
    /// <remarks>
    /// The length decides whether the document is sent at all, in the one statement rather than in a second round
    /// trip: a bound applied after the column reached the client would have paid the transfer it exists to refuse.
    /// The cast is what makes both the measurement and the value the text the parser will read, rather than whatever
    /// the driver would map <c>jsonb</c> to.
    /// </remarks>
    private const string SelectRecord =
        """
        SELECT
            "DisplayName",
            octet_length("Document"::text) AS "Length",
            CASE WHEN octet_length("Document"::text) <= @maximumOctets THEN "Document"::text END AS "Document",
            "Version",
            "DocumentWrittenAtRuntime"
        FROM settings_accounts
        WHERE "Id" = @owner;
        """;

    /// <inheritdoc />
    public async Task<OwnerSettingsDocument?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An owner record is read for an owner, and the value names nobody.", nameof(owner));
        }

        await using var command = dataSource.CreateCommand(SelectRecord);
        command.Parameters.AddWithValue("owner", owner.Value);
        command.Parameters.AddWithValue("maximumOctets", OwnerSettingsDocument.MaximumOctets);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var documentOctets = reader.GetInt32(1);

        if (documentOctets > OwnerSettingsDocument.MaximumOctets)
        {
            throw new OwnerSettingsUnreadableException(string.Create(
                CultureInfo.InvariantCulture,
                $"The record of owner {owner.Value} is {documentOctets} octets, past the {OwnerSettingsDocument.MaximumOctets} MailFathom binds an owner's document from, so it was not read. An owner record is a page of declarations rather than a payload: check what wrote the settings_accounts row."));
        }

        return new OwnerSettingsDocument(
            owner,
            reader.GetString(0),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetBoolean(4));
    }
}
