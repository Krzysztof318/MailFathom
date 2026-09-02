// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Keeps one owner's contact book, and erases a record of it completely when they ask.</summary>
/// <remarks>
/// <para>
/// The two staging operations write through the caller's session and commit nothing, as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// requires of a write port: the caller decides the transaction, and the retry policy above it decides what a lost race
/// means. Which addresses one contact may hold is the domain's rule; which contact may hold an address is this store's,
/// enforced by a unique constraint rather than by a check before the insert, because two callers claiming one address
/// pass any such check.
/// </para>
/// <para>
/// Every operation names the owner whose book it acts on, and the store applies it rather than trusting the record it
/// was handed: an identifier that names a contact of somebody else's book is a book that holds no such contact, so a
/// write cannot cross from one owner into another by naming a row it read elsewhere. Which contact may hold an address
/// is therefore a rule within a book rather than across the table, and the unique constraint underneath leads with the
/// owner to say so.
/// </para>
/// <para>
/// Erasure joins a session for the same reason the other two do, and for one of its own: what it reports having removed
/// and what it removed are read in one transaction, so the count is a fact rather than a number that was true a moment
/// earlier. The rows derived from a contact go with it through the schema's own cascade rather than through a second
/// statement somebody remembers to write. It is the data-subject erasure path, so it removes rather than marks.
/// </para>
/// </remarks>
public interface IContactStore
{
    /// <summary>Stages a contact the owner's book does not yet hold.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="owner">The owner whose book the contact is written into.</param>
    /// <param name="contact">The contact to add.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the insert is staged; nothing is committed here.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context.</exception>
    Task AddAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken);

    /// <summary>Stages the held record being replaced by the one supplied, address rows included.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="owner">The owner whose book holds the record being replaced.</param>
    /// <param name="contact">The contact as it is to stand, identified by its own identity.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns><see langword="true" /> when that owner's book held the contact and the replacement was staged; <see langword="false" /> when it holds none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context.</exception>
    /// <remarks>
    /// An address the record no longer names is removed rather than left behind, which is what makes a replacement the
    /// whole record instead of an addition to it. Two amendments of one contact are last-writer-wins, which is what an
    /// amendment stating the whole record means: the later one is the record. What is not left to that is a contact
    /// erased while an amendment was in flight — the row's concurrency token turns that into a conflict, so the retry
    /// reads a book holding nobody and answers so instead of putting the person back.
    /// </remarks>
    Task<bool> ReplaceAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken);

    /// <summary>Erases one contact and everything derived from it.</summary>
    /// <param name="session">The session the erasure joins.</param>
    /// <param name="owner">The owner whose book the contact is erased from.</param>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed, including a book that held no such contact.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context.</exception>
    /// <remarks>
    /// Answering for a contact the book does not hold is a completed erasure rather than a failure: the state an owner
    /// asked for is the state the book is in, and reporting it as an error would only tell them whether somebody had
    /// already erased that person.
    /// </remarks>
    Task<ContactErasure> EraseAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        ContactId contactId,
        CancellationToken cancellationToken);

    /// <summary>Erases every contact of the collected origin in one owner's book, and everything derived from them.</summary>
    /// <param name="session">The session the erasure joins.</param>
    /// <param name="owner">The owner whose collected half is erased.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context.</exception>
    /// <remarks>
    /// The asserted half is untouched, which is the whole point of the act: an owner who changed their mind about
    /// collection is undoing what their instance inferred rather than what they wrote. It is a set-based delete rather
    /// than a walk, because the alternative is loading a book of collected people into memory to remove it, and both
    /// counts are read in the same transaction that removes the rows so the answer is a fact rather than a number that
    /// was true a moment earlier.
    /// </remarks>
    Task<CollectedContactErasure> EraseCollectedAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        CancellationToken cancellationToken);
}
