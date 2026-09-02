// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads the object keys a deletion is about to make unreachable, and states them on the session.</summary>
/// <remarks>
/// <para>
/// Every payload row in this schema is removed by a cascade from the thing it belongs to, so the deletion path never
/// sees the row and therefore never sees the key. Reading the keys first is what puts them where the commit can act on
/// them: the row goes with the transaction, and the object goes immediately afterwards.
/// </para>
/// <para>
/// <b>Read inside the transaction, before the rows go.</b> After the commit there is nothing left to read — a key is
/// minted by the write that produced it and nothing about a row determines one, so a locator not collected here is a
/// locator that cannot be recovered at all. What that costs is one bounded query per erasure; what it buys is the
/// difference between erasure reaching the bucket and erasure waiting for a sweep to notice.
/// </para>
/// <para>
/// Only object-backed rows have anything to state. A deployment storing content in the database collects nothing here,
/// because its payload leaves with the row it is a column of.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class ReleasedContentObjects
{
    /// <summary>States the objects the payloads of one set of stored emails are held in.</summary>
    /// <param name="session">The session the deletion is staged in, which is what carries the keys to the commit.</param>
    /// <param name="storedEmailIds">The emails whose rows the caller is about to remove.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the session holds every key the deletion frees.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static async Task ReleaseForStoredEmailsAsync(
        IPersistenceSession session,
        IReadOnlyCollection<Guid> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count == 0)
        {
            return;
        }

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.EmailMessageContents
                .Where(content => storedEmailIds.Contains(content.StoredEmailId)
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);
    }

    /// <summary>States the object one draft's current revision is held in.</summary>
    /// <param name="session">The session the deletion is staged in.</param>
    /// <param name="mailDraftId">The draft the caller is about to remove.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the session holds the key the deletion frees.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One revision rather than every revision the draft ever had. A draft's earlier revisions are already orphans the
    /// moment they are superseded — each one was written under a key of its own — so what a discard frees is the
    /// current one alone and the sweep removes the rest.
    /// </remarks>
    public static async Task ReleaseForMailDraftAsync(
        IPersistenceSession session,
        Guid mailDraftId,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.MailDraftContents
                .Where(content => content.MailDraftId == mailDraftId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);
    }

    /// <summary>States every object holding mail one owner's erasure removes, across all four payload kinds.</summary>
    /// <param name="session">The session the erasure runs in.</param>
    /// <param name="ownerId">The owner being erased.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A task that completes once the session holds every key the erasure frees.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The four kinds are read as four queries because they hang off four different things, and all four are read whole
    /// rather than in pages: an owner's erasure removes an owner's whole mailbox, so what is held here is one string per
    /// stored payload for the length of one transaction. That is the price of answering a data subject truthfully about
    /// both stores, and it is paid once per erasure rather than per message.
    /// </remarks>
    public static async Task ReleaseForOwnerAsync(
        IPersistenceSession session,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // Narrowed on the owner each payload's own row carries rather than on the identifiers of the accounts that
        // owner holds. An identifier names one mailbox within its owner and another within the next, so a membership
        // test on it would release a second owner's objects whenever the two had named an account alike.
        await ReleaseAsync(
            session,
            sessionContext.EmailMessageContents
                .Where(content => content.StoredEmail.OwnerId == ownerId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.OutgoingEmailContents
                .Where(content => content.OutgoingEmail.OwnerId == ownerId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.MailDraftContents
                .Where(content => content.MailDraft.OwnerId == ownerId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.RecurringSendDrafts
                .Where(draft => draft.RecurringSend.OwnerId == ownerId
                    && draft.Backend == ContentStorageBackend.ObjectStorage)
                .Select(draft => draft.ObjectLocator!),
            cancellationToken);
    }

    private static async Task ReleaseAsync(
        IPersistenceSession session,
        IQueryable<string> objectLocators,
        CancellationToken cancellationToken)
    {
        var released = await objectLocators.ToArrayAsync(cancellationToken);

        if (released.Length > 0)
        {
            EfCorePersistenceSessionAccessor.SessionOf(session).ReleaseOnCommit(released);
        }
    }
}
