// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Stores raw email content outside ordinary email metadata queries.</summary>
/// <remarks>
/// <para>
/// No storage library publishes a contract for this seam, and the store behind it is expected to move from a
/// PostgreSQL table to object storage without a use case noticing, so the port names the operation in domain terms
/// instead. It takes the caller's session rather than opening one of its own, which is what makes a content write
/// commit or roll back together with the metadata row it belongs to.
/// </para>
/// <para>
/// Mail arriving and mail leaving are stored through the same port and are keyed differently, because they are the same
/// kind of payload owned by two different things: a synchronized message belongs to the local row that mirrors an
/// occurrence, and an outgoing email belongs to the record of the send it was composed for. One port is what keeps
/// raw MIME behind one seam, so the move to object storage is one adapter's rather than two.
/// </para>
/// </remarks>
public interface IEmailContentStore
{
    /// <summary>Puts one raw MIME payload wherever this deployment writes content next, before any unit of work is open.</summary>
    /// <param name="kind">Which of the four payload kinds is being placed.</param>
    /// <param name="rawMime">The raw RFC 822 bytes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>Where the payload was put, and what was measured over it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <remarks>
    /// <para>
    /// <b>A caller calls this before it opens its unit of work, never inside one.</b> That is the whole reason the
    /// placement is a step of its own: under the object backend this reaches the network, and a database transaction
    /// may not be held open across a call to a remote store. Every caller here has a transaction open by the time it
    /// reaches one of the write methods below, because the repository that mints the owning row's identity has already
    /// run — so the only moment at which the object can legally be written is before all of it.
    /// </para>
    /// <para>
    /// It is also what keeps a replay off the endpoint. <c>OptimisticConcurrencyRetryPolicy</c> repeats the caller's
    /// whole unit of work, and this call sits outside what it repeats, so every attempt records the same placement over
    /// the same object and no attempt writes bytes a second time.
    /// </para>
    /// <para>
    /// A placement whose unit of work never commits — one abandoned, or one that resolved to a record already carrying
    /// a payload — leaves an object nothing points at. That is the designed failure rather than a leak: no reader can
    /// observe it, and reclamation removes it once it is older than the configured age floor.
    /// </para>
    /// <para>
    /// Under the database backend this reaches no store at all. It measures the payload and hands it back, so that
    /// backend keeps committing content and metadata in one transaction exactly as it always has.
    /// </para>
    /// </remarks>
    Task<PlacedEmailContent> PlaceContentAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken);

    /// <summary>Saves raw MIME content idempotently for one locally stored email.</summary>
    /// <param name="session">The explicit persistence session this content write participates in.</param>
    /// <param name="storedEmailId">The stable local identifier of the corresponding metadata row.</param>
    /// <param name="occurrenceId">The remote occurrence the payload was fetched from, which the row is checked against.</param>
    /// <param name="placedContent">What <see cref="PlaceContentAsync" /> answered for this payload.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row under <paramref name="storedEmailId" /> mirrors a different occurrence.</exception>
    Task SaveContentAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrenceId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken);

    /// <summary>Reads back the raw MIME stored for one locally stored email, with what was recorded about it.</summary>
    /// <param name="storedEmailId">The stable local identifier of the metadata row.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stored content, or <see langword="null" /> when no content is stored for that email.</returns>
    /// <remarks>
    /// The read joins no session, because it participates in no write and a transaction held open across it would only
    /// widen a lock around a large payload. Absent content is an ordinary answer rather than a failure: an occurrence
    /// whose message exceeded the size limit is recorded with its metadata and no content at all.
    /// <para>
    /// The recorded length and digest come back with the payload rather than being verified here, so a caller that
    /// serves mail to a reader can tell a damaged local copy apart from an absent one and act on the difference.
    /// </para>
    /// </remarks>
    Task<StoredEmailContent?> FindStoredContentAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);

    /// <summary>Saves the raw MIME one outgoing email will be transmitted as, once and only once.</summary>
    /// <param name="session">The explicit persistence session this content write participates in.</param>
    /// <param name="outgoingEmailId">The record of the send this message was composed for.</param>
    /// <param name="placedContent">What <see cref="PlaceContentAsync" /> answered for this payload.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="placedContent" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no outgoing record carries <paramref name="outgoingEmailId" />.</exception>
    /// <remarks>
    /// <para>
    /// A message already stored for this record is left exactly as it is, and that is the contract rather than a
    /// tolerated repeat. A retry has to transmit the bytes an earlier attempt may already have begun transmitting: a
    /// message recomposed between attempts carries a different <c>Message-ID</c>, which turns one message into two in
    /// every recipient's thread, and rewriting the payload under a record that is mid-transmission would change what
    /// was sent after it was sent.
    /// </para>
    /// <para>
    /// It joins the caller's session for the reason the incoming write does, and more strongly: a record whose message
    /// was never stored has nothing to transmit, so the two commit together or neither does.
    /// </para>
    /// </remarks>
    Task SaveOutgoingContentAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken);

    /// <summary>Reads back the raw MIME stored for one outgoing email, with what was recorded about it.</summary>
    /// <param name="outgoingEmailId">The record of the send.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stored content, or <see langword="null" /> when no content is stored for that record.</returns>
    /// <remarks>
    /// This is what an attempt transmits, including a retry, which is what keeps a resumed send the same message rather
    /// than a second one that looks like it. Absent content is a defect here rather than an ordinary answer, unlike the
    /// incoming read: an outgoing record is written together with its message, so a record without one describes a send
    /// that can never happen — and the caller is the one that decides what to do about that.
    /// </remarks>
    Task<StoredEmailContent?> FindOutgoingContentAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Saves the draft every occurrence of one recurring send is composed from, once and only once.</summary>
    /// <param name="session">The explicit persistence session this content write participates in.</param>
    /// <param name="recurringSendId">The declaration the draft belongs to.</param>
    /// <param name="placedContent">What <see cref="PlaceContentAsync" /> answered for this payload.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="placedContent" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no declaration carries <paramref name="recurringSendId" />.</exception>
    /// <remarks>
    /// A draft is RFC 822 and therefore lives here rather than in a table of its own, which is what keeps every piece
    /// of mail content this system holds behind one port with one set of retention, erasure, and export obligations. It
    /// is not a message: nothing transmits it, and what each occasion transmits is composed from it with an identity
    /// and a date of that occasion's own.
    /// </remarks>
    Task SaveRecurringSendDraftAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken);

    /// <summary>Reads back the draft stored for one recurring send.</summary>
    /// <param name="recurringSendId">The declaration whose draft is read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stored draft, or <see langword="null" /> when the declaration has none.</returns>
    /// <remarks>
    /// Absent content is a defect rather than an ordinary answer, as it is for an outgoing record: a declaration is
    /// written together with its draft, so one without a draft describes occasions that could never produce a message.
    /// </remarks>
    Task<StoredEmailContent?> FindRecurringSendDraftAsync(
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken);

    /// <summary>Saves the raw MIME one revision of a draft is held as, replacing whatever the previous revision stored.</summary>
    /// <param name="session">The explicit persistence session this content write participates in.</param>
    /// <param name="draftId">The draft this message is the current revision of.</param>
    /// <param name="placedContent">What <see cref="PlaceContentAsync" /> answered for this payload.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="placedContent" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no draft is held under <paramref name="draftId" />.</exception>
    /// <remarks>
    /// <para>
    /// This is the one raw-MIME write in this system that overwrites, and the difference from the outgoing one is the
    /// whole reason a draft is not an outgoing record. A send's payload is written once because a retry has to transmit
    /// the bytes an earlier attempt may already have begun transmitting; a draft's payload is what its author is still
    /// editing, and holding every version of it would keep a message per keystroke for as long as the draft lives.
    /// </para>
    /// <para>
    /// It joins the caller's session because the payload and the revision it belongs to are one decision: a draft whose
    /// row says revision three and whose bytes are revision two is a draft that would be appended wrongly and promoted
    /// wrongly, so the two commit together or neither does.
    /// </para>
    /// </remarks>
    Task SaveMailDraftContentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken);

    /// <summary>Reads back the raw MIME stored for the current revision of one draft.</summary>
    /// <param name="draftId">The draft to read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The stored content, or <see langword="null" /> when no draft content is stored under that identifier.</returns>
    /// <remarks>
    /// It is what the drafts folder is appended from and what a promotion transmits, which is what keeps the message an
    /// owner reads in their own mail client and the message their correspondent receives the same bytes.
    /// </remarks>
    Task<StoredEmailContent?> FindMailDraftContentAsync(MailDraftId draftId, CancellationToken cancellationToken);
}
