// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Keeps, in PostgreSQL, the account of every copy of an outgoing message MailFathom put into a folder.</summary>
/// <remarks>
/// <para>
/// The writes an append makes use the context enlisted in the caller's session, so a row saying a copy may be in a
/// folder is only ever durable inside the transaction that decided to put it there. The read a synchronization run
/// issues uses the scoped context, because it joins no transaction.
/// </para>
/// <para>
/// The exception is <see cref="RecordFilingFailureAsync" />, which is given no session by the port. It writes beside a
/// delivery that has already been committed, and a failure to file a copy must never be able to roll one back.
/// </para>
/// <para>
/// Nothing here is guarded by a read-then-write. The key is the record and the place together, so a second attempt to
/// file the same message into the same place is refused by the database rather than by a check two callers can pass
/// between — and a second copy in somebody's sent folder is a message they read as one they sent twice.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutgoingMailFilingStore(MailFathomDbContext readContext) : IOutgoingMailFilingStore
{
    /// <inheritdoc />
    public async Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        MailFolderResolution destination,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destination);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        var record = await writeContext.OutgoingEmails.FindAsync([outgoingEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No outgoing email record carries the identifier {outgoingEmailId}, so no copy of it can be filed.");

        if (await FindAsync(writeContext, outgoingEmailId, filing, cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                $"Outgoing email record {outgoingEmailId} already has a copy filed as {filing.Name}.");
        }

        // Added through the record's own collection, so the row is inserted as a child of the record it accounts for
        // and can never be committed against a record that is not there.
        record.Filings.Add(new OutgoingEmailFilingEntity
        {
            OutgoingEmailId = outgoingEmailId.Value,
            OutgoingEmail = record,
            Filing = filing.Name,
            MailboxAccountId = record.MailboxAccountId,
            FolderAlias = destination.Alias.Value,
            FolderPath = destination.RemotePath.Value,
            Stage = OutgoingMailFilingStage.Issued,
            AppendedAt = appendedAt,
        });
    }

    /// <inheritdoc />
    public async Task RecordAppendConfirmedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        AppendedMailCopy copy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(copy);

        var entity = await RequireAsync(session, outgoingEmailId, filing, cancellationToken);

        if (entity.Stage != OutgoingMailFilingStage.Issued)
        {
            throw new InvalidOperationException(
                $"The copy of outgoing email record {outgoingEmailId} filed as {filing.Name} is at stage {entity.Stage}, and a confirmation follows {OutgoingMailFilingStage.Issued}.");
        }

        entity.Stage = OutgoingMailFilingStage.Confirmed;
        entity.PlacementUidValidity = copy.Placement.UidValidity?.Value;
        entity.PlacementUid = copy.Placement.Uid?.Value;
        entity.InternetMessageId = copy.InternetMessageId;
    }

    /// <inheritdoc />
    public async Task RecordWithdrawnAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken)
    {
        var entity = await RequireAsync(session, outgoingEmailId, filing, cancellationToken);

        entity.Stage = OutgoingMailFilingStage.Withdrawn;
        entity.WithdrawnAt ??= withdrawnAt;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement rather than a loaded row, because it writes a column no other writer touches and must not turn an
    /// overlap with the delivery's own writes into a conflict the caller has to retry. A record that is no longer there
    /// is left alone: the statement writes nothing and reports it, which is what an erasure that ran between the
    /// delivery and this looks like.
    /// </remarks>
    public Task RecordFilingFailureAsync(
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken) =>
        readContext.OutgoingEmails
            .Where(message => message.Id == outgoingEmailId.Value)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.LastFilingFailureCode, failure.Value),
                cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// What the answer can hold is bounded by what was asked: every row it matches is named by one of the two
    /// collections, which a synchronization run fills from one batch of discoveries.
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingMailFilingRecord>> ReadFilingsAtAsync(
        MailAccountId accountId,
        RemoteFolderPath folderPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        IReadOnlyCollection<string> internetMessageIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);
        ArgumentNullException.ThrowIfNull(internetMessageIds);

        if (uids.Count == 0 && internetMessageIds.Count == 0)
        {
            return [];
        }

        var accountValue = accountId.Value;
        var folderValue = folderPath.Value;
        var uidValidityValue = uidValidity.Value;

        // Nullable so the comparison is against the column as it is stored: a row whose server named no placement holds
        // null there and must match nothing, rather than being coerced into a UID it never reported.
        uint?[] placedUids = [.. uids.Select(static uid => (uint?)uid.Value)];
        string[] messageIds = [.. internetMessageIds];

        var entities = await readContext.OutgoingEmailFilings
            .AsNoTracking()
            .Where(candidate => candidate.MailboxAccountId == accountValue
                && candidate.FolderPath == folderValue
                && candidate.Stage == OutgoingMailFilingStage.Confirmed
                && candidate.ObservedAt == null
                && ((candidate.PlacementUidValidity == uidValidityValue
                        && placedUids.Contains(candidate.PlacementUid))
                    || (candidate.InternetMessageId != null
                        && messageIds.Contains(candidate.InternetMessageId))))
            .OrderBy(candidate => candidate.AppendedAt)
            .ThenBy(candidate => candidate.OutgoingEmailId)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(OutgoingEmailRecordMapping.ToFiling)];
    }

    /// <inheritdoc />
    public async Task RecordFilingObservedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var entity = await RequireAsync(session, outgoingEmailId, filing, cancellationToken);

        entity.ObservedAt ??= observedAt;
    }

    /// <summary>Resolves one filing row through the primary key, which is the record and the place together.</summary>
    /// <remarks>
    /// <c>FindAsync</c> rather than a query, so a row this same session inserted moments earlier is resolved from the
    /// change tracker: the append is issued and confirmed inside one attempt, and the second write would otherwise be
    /// looking for a row that is not committed yet.
    /// </remarks>
    private static Task<OutgoingEmailFilingEntity?> FindAsync(
        MailFathomDbContext writeContext,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken) =>
        writeContext.OutgoingEmailFilings
            .FindAsync([outgoingEmailId.Value, filing.Name], cancellationToken)
            .AsTask();

    private static async Task<OutgoingEmailFilingEntity> RequireAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        return await FindAsync(writeContext, outgoingEmailId, filing, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Outgoing email record {outgoingEmailId} has no copy filed as {filing.Name}.");
    }
}
