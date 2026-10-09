// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Folders;
using MailFathom.Application.SensitiveContent;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Persistence.Connections;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>Answers every question about all the accounts this deployment serves from the columns each account's document was read into.</summary>
/// <remarks>
/// <para>
/// A singleton over the pool rather than a scoped context, because what asks is as often a singleton — the scanning
/// postures, the reload of the rule set — as a work unit, and every answer is one short statement that joins no
/// transaction. The statements are written out rather than composed, so what runs against the database is exactly what
/// is read here.
/// </para>
/// <para>
/// The served predicate is one fragment every statement shares, which is what keeps every answer about the same set of
/// accounts.
/// </para>
/// <para>
/// The pool arrives behind a delegate rather than resolved, because the scanning postures are built while the hosted
/// services are constructed, before startup has composed the connection string the pool is built from.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedServedMailAccounts(
    Func<NpgsqlDataSource> dataSource,
    DatabaseCommandTimeout commandTimeout)
    : IServedMailAccountReader, IDeploymentMailFolders
{
    private const string Served =
        """
        account."EmailAddress" IS NOT NULL
        AND account."HasReadableSettings"
        AND EXISTS (SELECT 1 FROM mail_account_assignments AS assignment WHERE assignment."MailAccountId" = account."Id")
        """;

    private const string SelectServed =
        $"""
         SELECT account."Id", account."DisplayName", account."SynchronizationMode"
         FROM settings_mail_accounts AS account
         WHERE {Served}
         ORDER BY account."Id";
         """;

    private const string SelectServedRecords =
        $"""
         SELECT account."Id", account."EmailAddress", account."DisplayName", account."Document"::text, account."Version"
         FROM settings_mail_accounts AS account
         WHERE {Served}
           AND octet_length(account."Document"::text) <= @maximumOctets
         ORDER BY account."Id";
         """;

    private const string SelectScanningRequests =
        $"""
         SELECT DISTINCT account."ScansFor", account."ScreensOutgoingMailFor"
         FROM settings_mail_accounts AS account
         WHERE {Served}
           AND (@everyUser OR EXISTS (
               SELECT 1 FROM mail_account_assignments AS held
               WHERE held."MailAccountId" = account."Id" AND held."UserId" = @user));
         """;

    private const string SelectFolders =
        $"""
         SELECT folder."MailAccountId", folder."Alias"
         FROM mail_account_folder_settings AS folder
         JOIN settings_mail_accounts AS account ON account."Id" = folder."MailAccountId"
         WHERE {Served}
           AND (@everyAccount OR folder."MailAccountId" = ANY(@accounts))
           AND CASE @selection
               WHEN '{nameof(MailFolderSelection.Mapped)}' THEN TRUE
               WHEN '{nameof(MailFolderSelection.Synchronized)}' THEN folder."IsSynchronized"
               WHEN '{nameof(MailFolderSelection.VisibleToTools)}' THEN folder."IsVisibleToTools"
               WHEN '{nameof(MailFolderSelection.GeneratingEmbeddings)}' THEN folder."GeneratesEmbeddings"
               WHEN '{nameof(MailFolderSelection.Junk)}' THEN folder."SpecialUse" IS NOT DISTINCT FROM @junk
               WHEN '{nameof(MailFolderSelection.OfAccountsClassifyingSpam)}' THEN account."ClassifiesSpam"
               WHEN '{nameof(MailFolderSelection.ClassifiedForSpam)}' THEN account."ClassifiesSpam" AND folder."IsClassifiedForSpam"
               ELSE FALSE
           END
         ORDER BY folder."MailAccountId", folder."Alias";
         """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServedMailAccountRow>> ReadServedAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectServed, connection) { CommandTimeout = this.CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var served = new List<ServedMailAccountRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            served.Add(new ServedMailAccountRow(
                reader.GetGuid(0),
                reader.GetString(1),
                (MailSynchronizationMode)reader.GetInt32(2)));
        }

        return [.. served.OrderBy(account => account.Id.ToString("D"), StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A document past <see cref="UserSettingsDocument.MaximumOctets" /> is left in the database rather than sent, and so
    /// is left out of the answer, because it is the record the roster refuses to bind too: no write of this build leaves
    /// one, and an account the roster does not serve has no rule judged against it.
    /// </para>
    /// <para>
    /// It takes no ceiling on the number of accounts, for the reason <see cref="IMailAccountRecordStore.ReadSolelyAssignedAsync" />
    /// takes none: what asks is judging a rule set against every account it could reach, and a truncated answer would
    /// pass a rule that names an account past the cut. What bounds it is the deployment — the accounts assigned to the
    /// users it holds, at most <see cref="MailAccountRecord.MaximumAssignedPerUser" /> each — under that bound per
    /// document.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MailAccountRecord>> ReadServedRecordsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectServedRecords, connection) { CommandTimeout = this.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("maximumOctets", UserSettingsDocument.MaximumOctets);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var records = new List<MailAccountRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new MailAccountRecord(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4)));
        }

        return [.. records.OrderBy(account => account.Id.ToString("D"), StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailAccountScanningRequest>> ReadScanningRequestsAsync(
        UserId? user,
        CancellationToken cancellationToken)
    {
        if (user is { IsSpecified: false })
        {
            throw new ArgumentException("The accounts of a named user are asked about, or every account.", nameof(user));
        }

        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectScanningRequests, connection) { CommandTimeout = this.CommandTimeoutSeconds };

        command.Parameters.AddWithValue("everyUser", user is null);
        command.Parameters.AddWithValue("user", user?.Value ?? Guid.Empty);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var requests = new List<MailAccountScanningRequest>();

        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(new MailAccountScanningRequest(
                ToScannerKinds(reader.GetFieldValue<int[]>(0)),
                ToScannerKinds(reader.GetFieldValue<int[]>(1))));
        }

        return requests;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailFolderIdentity>> ReadAsync(
        MailFolderSelection selection,
        CancellationToken cancellationToken) =>
        this.ReadFoldersAsync(selection, accounts: null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MailFolderIdentity>> ReadAsync(
        MailFolderSelection selection,
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return this.ReadFoldersAsync(
            selection,
            [
                .. accounts
                    .Select(account => Guid.TryParse(account.Value, out var id) ? id : (Guid?)null)
                    .OfType<Guid>(),
            ],
            cancellationToken);
    }

    private static SensitiveContentScannerKind[] ToScannerKinds(int[] column) =>
    [
        .. column
            .Select(value => (SensitiveContentScannerKind)value)
            .Where(scanner => Enum.IsDefined(scanner)),
    ];

    private async Task<IReadOnlyList<MailFolderIdentity>> ReadFoldersAsync(
        MailFolderSelection selection,
        Guid[]? accounts,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(selection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection),
                selection,
                "A folder set is read for what its folders take part in, and no other value names one.");
        }

        await using var connection = await dataSource().OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectFolders, connection) { CommandTimeout = this.CommandTimeoutSeconds };

        command.Parameters.AddWithValue("everyAccount", accounts is null);
        command.Parameters.AddWithValue("accounts", (accounts ?? []).Distinct().ToArray());
        command.Parameters.AddWithValue("selection", selection.ToString());
        command.Parameters.AddWithValue("junk", (int)MailFolderSpecialUse.Junk);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var folders = new List<MailFolderIdentity>();

        while (await reader.ReadAsync(cancellationToken))
        {
            folders.Add(new MailFolderIdentity(
                MailAccountId.Create(reader.GetGuid(0).ToString("D")),
                MailFolderAlias.Create(reader.GetString(1))));
        }

        return folders;
    }

    private int CommandTimeoutSeconds => (int)commandTimeout.Value.TotalSeconds;
}
