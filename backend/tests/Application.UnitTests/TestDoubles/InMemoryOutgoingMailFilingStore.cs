// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the copies a test's filer has put into folders, in memory, with the real store's own refusals.</summary>
/// <remarks>
/// <para>
/// The refusals are what make it worth writing rather than substituting. The identity of a filing is the record and the
/// place together, and the real store leaves that to a primary key, so a double that quietly accepted a second row
/// would let a test pass over the one failure this feature exists to prevent — a second copy of somebody's message in
/// their own sent folder.
/// </para>
/// <para>
/// The rows are written back onto the outgoing record as well, because that is how the real store publishes them: they
/// hang off the record and come back with it, and a pass reads what an earlier append wrote through that navigation
/// rather than through this port.
/// </para>
/// </remarks>
internal sealed class InMemoryOutgoingMailFilingStore(InMemoryOutgoingEmailStore outgoingEmails)
    : IOutgoingMailFilingStore
{
    private readonly Dictionary<(OutgoingEmailId Record, string Filing), OutgoingMailFilingRecord> rows = [];
    private readonly Dictionary<OutgoingEmailId, MailFathomErrorCode> failures = [];

    /// <summary>Reads back what this store holds about one record's copies.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <returns>Its filings, in no particular order.</returns>
    internal IReadOnlyList<OutgoingMailFilingRecord> Read(OutgoingEmailId outgoingEmailId) =>
        [.. this.rows.Values.Where(row => row.OutgoingEmailId == outgoingEmailId)];

    /// <summary>Reads back the failure the last filing attempt on one record recorded, if any.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <returns>The code, or <see langword="null" /> when nothing has failed.</returns>
    internal MailFathomErrorCode? ReadFailure(OutgoingEmailId outgoingEmailId) =>
        this.failures.TryGetValue(outgoingEmailId, out var failure) ? failure : null;

    public Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        MailFolderResolution destination,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destination);

        var key = KeyOf(outgoingEmailId, filing);

        if (this.rows.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Outgoing email record {outgoingEmailId} already has a copy filed as {filing.Name}.");
        }

        this.rows[key] = new OutgoingMailFilingRecord
        {
            OutgoingEmailId = outgoingEmailId,
            Filing = filing,
            FolderAlias = destination.Alias,
            FolderPath = destination.RemotePath,
            Stage = OutgoingMailFilingStage.Issued,
            Placement = RemoteEmailPlacement.NotReported(),
            InternetMessageId = null,
            AppendedAt = appendedAt,
            ObservedAt = null,
            WithdrawnAt = null,
        };

        this.Publish(outgoingEmailId);

        return Task.CompletedTask;
    }

    public Task RecordAppendConfirmedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        AppendedMailCopy copy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(copy);

        var row = this.Require(outgoingEmailId, filing);

        if (row.Stage != OutgoingMailFilingStage.Issued)
        {
            throw new InvalidOperationException(
                $"The copy of outgoing email record {outgoingEmailId} filed as {filing.Name} is at stage {row.Stage}.");
        }

        this.Write(row with
        {
            Stage = OutgoingMailFilingStage.Confirmed,
            Placement = copy.Placement,
            InternetMessageId = copy.InternetMessageId,
        });

        return Task.CompletedTask;
    }

    public Task RecordWithdrawnAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var row = this.Require(outgoingEmailId, filing);

        this.Write(row with
        {
            Stage = OutgoingMailFilingStage.Withdrawn,
            WithdrawnAt = row.WithdrawnAt ?? withdrawnAt,
        });

        return Task.CompletedTask;
    }

    public Task RecordFilingFailureAsync(
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        this.failures[outgoingEmailId] = failure;
        this.Publish(outgoingEmailId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutgoingMailFilingRecord>> ReadFilingsAtAsync(
        MailAccountId accountId,
        RemoteFolderPath folderPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        IReadOnlyCollection<string> internetMessageIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);
        ArgumentNullException.ThrowIfNull(internetMessageIds);

        IReadOnlyList<OutgoingMailFilingRecord> found =
        [
            .. this.rows.Values
                .Where(row => this.AccountOf(row) == accountId
                    && row.Stage == OutgoingMailFilingStage.Confirmed
                    && row.ObservedAt is null
                    && (uids.Any(uid => row.AccountsForPlacementAt(folderPath, uidValidity, uid))
                        || internetMessageIds.Any(messageId => row.AccountsForMessageAt(folderPath, messageId))))
                .OrderBy(row => row.AppendedAt),
        ];

        return Task.FromResult(found);
    }

    public Task RecordFilingObservedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var row = this.Require(outgoingEmailId, filing);

        this.Write(row with { ObservedAt = row.ObservedAt ?? observedAt });

        return Task.CompletedTask;
    }

    private static (OutgoingEmailId Record, string Filing) KeyOf(
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing) => (outgoingEmailId, filing.Name);

    private MailAccountId AccountOf(OutgoingMailFilingRecord row) =>
        outgoingEmails.Read(row.OutgoingEmailId).AccountId;

    private OutgoingMailFilingRecord Require(OutgoingEmailId outgoingEmailId, OutgoingMailFiling filing) =>
        this.rows.TryGetValue(KeyOf(outgoingEmailId, filing), out var row)
            ? row
            : throw new InvalidOperationException(
                $"Outgoing email record {outgoingEmailId} has no copy filed as {filing.Name}.");

    private void Write(OutgoingMailFilingRecord row)
    {
        this.rows[KeyOf(row.OutgoingEmailId, row.Filing)] = row;
        this.Publish(row.OutgoingEmailId);
    }

    private void Publish(OutgoingEmailId outgoingEmailId) =>
        outgoingEmails.SetFilings(
            outgoingEmailId,
            this.Read(outgoingEmailId),
            this.ReadFailure(outgoingEmailId));
}
