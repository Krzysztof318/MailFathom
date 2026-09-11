// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads one user's record out of the single <c>settings_accounts</c> row that holds it.</summary>
/// <remarks>
/// <para>
/// The lookup is by primary key, which is what the routing decision buys: one user is one row, so a user-scoped
/// view costs one seek rather than a scan of every user's settings or a value-per-key query. Nothing here reads into
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
/// The statement names the user as a parameter and composes no identifier from anything a caller supplied, so there
/// is nothing here for a value to reach.
/// </para>
/// <para>
/// Every way the database declines to answer arrives as one refusal naming a place to look, because the caller of a
/// port is holding a user rather than a connection: what a failed read tells the operator is
/// <see cref="UserSettingsReadFailures" />'s decision, and the driver's own text — which can name the database, the
/// role, or the table — stays in the inner failure. The connection is opened in a step of its own so that the two
/// stages can be told apart, and the statement carries the deployment's configured command timeout, which nothing
/// puts onto this data source.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedUserSettingsDocumentReader(
    NpgsqlDataSource dataSource,
    DatabaseCommandTimeout commandTimeout)
    : IUserSettingsDocumentReader
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
            "Version"
        FROM settings_accounts
        WHERE "Id" = @user;
        """;

    /// <inheritdoc />
    public async Task<UserSettingsDocument?> ReadAsync(MailUserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user record is read for a user, and the value names nobody.", nameof(user));
        }

        // Opened in a step of its own rather than left to the command, because the driver reports a connect timeout, a
        // pool with nothing left to hand out, and a statement the server never answered as the same shape — and the
        // first two are a database that could not be reached while the third is one that was. Only this side knows
        // which stage failed, so each stage reports what its own failure can mean.
        NpgsqlConnection connection;

        try
        {
            connection = await dataSource.OpenConnectionAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new UserSettingsUnreadableException(
                UserSettingsReadFailures.DiagnoseWhileConnecting(exception),
                exception);
        }

        await using (connection)
        {
            try
            {
                await using var command = new NpgsqlCommand(SelectRecord, connection);

                // Written onto the command rather than inherited from the pool, because nothing puts the configured
                // bound onto this data source: the setting reaches EF Core's commands through the enrichment that
                // configures its context, and this statement is not one of those. Without it the read would be
                // bounded by the driver's own default, or by nothing at all where a connection string carries
                // `Command Timeout=0` — while the diagnosis of a timeout sends the operator to a setting that
                // governed nothing.
                command.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;

                command.Parameters.AddWithValue("user", user.Value);
                command.Parameters.AddWithValue("maximumOctets", UserSettingsDocument.MaximumOctets);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                var documentOctets = reader.GetInt32(1);

                if (documentOctets > UserSettingsDocument.MaximumOctets)
                {
                    throw new UserSettingsUnreadableException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The record of user {user.Value} is {documentOctets} octets, past the {UserSettingsDocument.MaximumOctets} MailFathom binds a user's document from, so it was not read. A user record is a page of declarations rather than a payload: check what wrote the settings_accounts row."));
                }

                return new UserSettingsDocument(
                    user,
                    reader.GetString(0),
                    reader.GetString(2),
                    reader.GetInt64(3));
            }
            catch (NpgsqlException exception)
            {
                throw new UserSettingsUnreadableException(
                    UserSettingsReadFailures.DiagnoseWhileReading(exception),
                    exception);
            }
        }
    }
}
