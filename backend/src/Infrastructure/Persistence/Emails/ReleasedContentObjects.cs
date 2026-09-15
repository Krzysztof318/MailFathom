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

    /// <summary>States every object one user's own erasure removes: what they were writing, and the files they added.</summary>
    /// <param name="session">The session the erasure runs in.</param>
    /// <param name="userId">The user being erased.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A task that completes once the session holds every key the erasure frees.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Only what the user authored and what they uploaded. Stored mail and submitted messages are the mailbox's rather
    /// than theirs, so those payloads are released when an account is erased and not before — an account somebody else
    /// is still assigned keeps every one of them. The three kinds are read as three queries because they hang off
    /// three different things, and each whole rather than in pages: what is held is one string per stored payload for
    /// the length of one transaction, which is the price of answering a data subject truthfully about both stores.
    /// </remarks>
    public static async Task ReleaseForUserAsync(
        IPersistenceSession session,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.MailDraftContents
                .Where(content => content.MailDraft.UserId == userId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.RecurringSendDrafts
                .Where(draft => draft.RecurringSend.UserId == userId
                    && draft.Backend == ContentStorageBackend.ObjectStorage)
                .Select(draft => draft.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.StoredFiles
                .Where(file => file.UserId == userId && file.Backend == ContentStorageBackend.ObjectStorage)
                .Select(file => file.ObjectLocator!),
            cancellationToken);
    }

    /// <summary>Releases the object-stored payloads of one mail account, which is one copy however many read it.</summary>
    /// <param name="session">The transaction the erasure runs in.</param>
    /// <param name="accountId">The account whose payloads are released.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A task that completes once every locator is registered for release on commit.</returns>
    public static async Task ReleaseForMailAccountAsync(
        IPersistenceSession session,
        string accountId,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.EmailMessageContents
                .Where(content => content.StoredEmail.MailboxAccountId == accountId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.OutgoingEmailContents
                .Where(content => content.OutgoingEmail.MailboxAccountId == accountId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.MailDraftContents
                .Where(content => content.MailDraft.MailboxAccountId == accountId
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await ReleaseAsync(
            session,
            sessionContext.RecurringSendDrafts
                .Where(draft => draft.RecurringSend.MailboxAccountId == accountId
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
