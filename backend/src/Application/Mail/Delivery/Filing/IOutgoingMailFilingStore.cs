// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Keeps the durable account of every copy of an outgoing message MailFathom has put into a folder.</summary>
/// <remarks>
/// <para>
/// The rows hang off the outgoing record and are read back with it, so a caller asking what became of a send learns
/// where its copies are from the same read. What this port adds is the two things that read is the wrong shape for: the
/// writes one append makes as it happens, and the query a synchronization run issues about a folder rather than about a
/// record.
/// </para>
/// <para>
/// The identity of a filing is the outgoing record and the place together, which is what makes the first write of an
/// append idempotent: asking to file the same message into the same place twice reaches one row, and a second copy in
/// somebody's folder is exactly what that prevents.
/// </para>
/// <para>
/// Nothing here returns anything derived from a message. A folder, an alias, a UID, and an identity MailFathom minted
/// itself are what a row holds, which is what lets provenance be established without reading mail.
/// </para>
/// </remarks>
public interface IOutgoingMailFilingStore
{
    /// <summary>Writes down that an append is about to be issued, before the command goes out.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingEmailId">The record the copy is filed from.</param>
    /// <param name="filing">Which place the copy is going into.</param>
    /// <param name="destination">The folder binding the copy is appended to.</param>
    /// <param name="appendedAt">When the append was issued.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is durable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="destination" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, or when this place is already filed.</exception>
    /// <remarks>
    /// It is written first for the reason a mutation record is: a process that dies between this write and the server's
    /// answer leaves a statement that the copy may be in the folder, and nothing appends again on the strength of it.
    /// </remarks>
    Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        MailFolderResolution destination,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken);

    /// <summary>Writes down what the server said about the copy it accepted.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingEmailId">The record the copy was filed from.</param>
    /// <param name="filing">Which place the copy went into.</param>
    /// <param name="copy">What the server named, which may name no placement at all.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is confirmed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="copy" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no row carries that identity, or when it is not awaiting confirmation.</exception>
    Task RecordAppendConfirmedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        AppendedMailCopy copy,
        CancellationToken cancellationToken);

    /// <summary>Writes down that the copy has been taken back out of its folder.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingEmailId">The record the copy was filed from.</param>
    /// <param name="filing">Which place the copy was in.</param>
    /// <param name="withdrawnAt">When the folder stopped holding it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row is marked withdrawn.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no row carries that identity.</exception>
    Task RecordWithdrawnAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken);

    /// <summary>Records why the last filing attempt for one send did not put a copy anywhere.</summary>
    /// <param name="outgoingEmailId">The record whose copy could not be filed.</param>
    /// <param name="failure">The code identifying what ended the attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the code is on the record.</returns>
    /// <remarks>
    /// <para>
    /// It takes no session and moves no stage, deliberately. A send whose copy could not be filed is a send that
    /// happened, so the delivery stage stands exactly where the submission left it and this is written beside it.
    /// </para>
    /// <para>
    /// Writing outside the caller's transaction is what keeps that true when the caller has none: filing runs after a
    /// delivery has been committed, and a failure to file must not be able to roll a delivery back.
    /// </para>
    /// </remarks>
    Task RecordFilingFailureAsync(
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);

    /// <summary>Reads the filings that put a copy into one folder at one of the occurrences a batch discovered.</summary>
    /// <param name="account">The account whose filings are read.</param>
    /// <param name="folderPath">The remote folder being synchronized, which is the folder those filings named.</param>
    /// <param name="uidValidity">The UIDVALIDITY that folder reports now.</param>
    /// <param name="uids">The UIDs one batch of the forward pass discovered.</param>
    /// <param name="internetMessageIds">The <c>Message-ID</c> values that batch reported, for the servers that name no placement.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every filing a discovery in that batch could belong to, which may be none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uids" /> or <paramref name="internetMessageIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A whole batch is asked about at once, so a folder nothing was ever filed into — which is nearly every folder on
    /// nearly every run — costs one query rather than one per message. Both halves of the join are asked in the same
    /// read, because a batch can carry discoveries of both kinds and a second query would double the cost of the case
    /// that answers nothing.
    /// </para>
    /// <para>
    /// Which row a given discovery belongs to is then decided by
    /// <see cref="OutgoingMailFilingRecord.AccountsForPlacementAt" /> and
    /// <see cref="OutgoingMailFilingRecord.AccountsForMessageAt" />, which restate every condition of the read rather
    /// than trusting it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutgoingMailFilingRecord>> ReadFilingsAtAsync(
        MailAccountIdentity account,
        RemoteFolderPath folderPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        IReadOnlyCollection<string> internetMessageIds,
        CancellationToken cancellationToken);

    /// <summary>Writes down that synchronization has met the copy one filing put in a folder.</summary>
    /// <param name="session">The session the write joins, which is the one the discovered email is stored in.</param>
    /// <param name="outgoingEmailId">The record the copy was filed from.</param>
    /// <param name="filing">Which place the copy was recognized in.</param>
    /// <param name="observedAt">When the run recognized it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the observation is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no row carries that identity.</exception>
    /// <remarks>
    /// It joins the transaction that stores the email, so a run that recorded the observation and then rolled the row
    /// back cannot leave a filing claiming to have been met by a message no local state holds. Writing it is also what
    /// takes the row out of the candidates a later discovery is matched against.
    /// </remarks>
    Task RecordFilingObservedAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingMailFiling filing,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);
}
