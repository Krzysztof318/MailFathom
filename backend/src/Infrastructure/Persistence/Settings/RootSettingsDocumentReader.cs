// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// <para>
/// What a failed read tells the operator is <see cref="RootSettingsReadFailures" />'s, not this class's: the command
/// needs a database and is proved against one, while the diagnosis is a decision worth stating in a unit test.
/// </para>
/// <para>
/// The document is bounded before it is expanded rather than after. It is the one value here whose size nothing else
/// constrains — the column takes what a writer put in it — and the expansion runs while the host composes its
/// configuration, where an allocation failure is a startup with no message rather than a refusal naming a limit.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class RootSettingsDocumentReader(NpgsqlDataSource dataSource) : IRootSettingsDocumentReader
{
    /// <summary>The largest persisted configuration document this build will compose settings from.</summary>
    /// <remarks>Stated on <see cref="RootSettingsDocument" /> so the write that produces a document is bounded by the same number the read composes from.</remarks>
    private const int MaximumDocumentOctets = RootSettingsDocument.MaximumOctets;

    /// <summary>Reads the singleton row, and the document with it only when the document is small enough to compose.</summary>
    /// <remarks>
    /// The length decides whether the document is sent at all, in the one statement rather than in a second round
    /// trip: a bound applied after the column reached the client would have paid the transfer it exists to refuse.
    /// The cast is what makes both the measurement and the value the text the parser will read, rather than whatever
    /// the driver would map <c>jsonb</c> to.
    /// </remarks>
    private const string SelectDocument =
        """
        SELECT
            octet_length("Document"::text) AS "Length",
            CASE WHEN octet_length("Document"::text) <= @maximumOctets THEN "Document"::text END AS "Document",
            "Version"
        FROM settings_root
        WHERE "Id" = @id;
        """;

    /// <inheritdoc />
    public async Task<RootSettingsDocument> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(SelectDocument);
            command.Parameters.AddWithValue("id", RootSettingsEntity.SingletonId);
            command.Parameters.AddWithValue("maximumOctets", MaximumDocumentOctets);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new RootSettingsUnreadableException(
                    "The persisted configuration row is missing from settings_root. Apply the migrations this build defines, which provision it, and start the host again.");
            }

            var documentOctets = reader.GetInt32(0);

            if (documentOctets > MaximumDocumentOctets)
            {
                throw new RootSettingsUnreadableException(
                    $"The persisted configuration document is {documentOctets} octets, past the {MaximumDocumentOctets} this build composes settings from, so it was not read. A configuration document is a page of settings rather than a payload: check what wrote the settings_root row.");
            }

            return new RootSettingsDocument(reader.GetString(1), reader.GetInt64(2));
        }
        catch (NpgsqlException exception)
        {
            throw new RootSettingsUnreadableException(RootSettingsReadFailures.Diagnose(exception), exception);
        }
    }
}
