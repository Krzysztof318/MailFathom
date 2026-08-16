// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>The acts a contact book supports: record a person, amend one, promote one, erase one, and export one.</summary>
/// <remarks>
/// <para>
/// Every surface over the book — the administration tool, the MCP tools, and collection from arriving mail — performs
/// these five acts and no others, which is what keeps the origin rule from being a convention each of them remembers.
/// A writer names the origin it acts under, and a contact is amendable only by a writer of its own: collection never
/// touches what an owner wrote down, and an owner promotes a collected contact rather than editing it in place.
/// Promotion names the writer for the same reason, so the act of taking a record on is the owner's rather than something
/// collection can perform on its own behalf.
/// </para>
/// <para>
/// Each write is idempotent from a fresh read and is committed through the optimistic concurrency policy, so two callers
/// claiming one address converge on the same answer instead of one of them meeting a provider failure: the loser's
/// insert violates the unique constraint over addresses, the retry re-reads, and the second caller is told which contact
/// holds it.
/// </para>
/// <para>
/// Nothing here logs. A name, an address, and a note are personal data about a third party, and the outcomes this type
/// produces are what a surface reports; a log line about a write would put the whole book into a log within a week of
/// somebody using it.
/// </para>
/// </remarks>
public sealed class ContactBook
{
    private readonly IContactStore store;
    private readonly IContactDirectory directory;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the book over the store it writes to and the directory it reads from.</summary>
    /// <param name="store">Keeps the records.</param>
    /// <param name="directory">Answers what the book already holds.</param>
    /// <param name="commitPolicy">Commits each write, retrying a lost race from a fresh read.</param>
    /// <param name="timeProvider">Stamps when a contact was recorded, amended, promoted, or exported.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBook(
        IContactStore store,
        IContactDirectory directory,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.directory = directory;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Records a person the book does not yet hold.</summary>
    /// <param name="newContact">The person to record and the origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record as written, or the contact that already holds one of its addresses.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="newContact" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the supplied addresses do not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <remarks>
    /// The identity is minted here, from a UUID version 7 over the instant of the write, so the book's own identifiers
    /// order the way the records were created without a caller being able to choose one.
    /// </remarks>
    public Task<ContactWriteResult> RecordAsync(NewContact newContact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newContact);

        var recordedAt = this.timeProvider.GetUtcNow();
        var contact = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(recordedAt)),
            newContact.DisplayName,
            newContact.Addresses,
            newContact.PreferredAddress,
            newContact.Note,
            newContact.Origin,
            recordedAt,
            recordedAt);

        return this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                if (await this.AddressHolderOtherThanAsync(contact, token) is { } holder)
                {
                    return ContactWriteResult.AddressHeldBy(holder);
                }

                await this.store.AddAsync(session, contact, token);

                return ContactWriteResult.Written(contact);
            },
            cancellationToken);
    }

    /// <summary>Amends a contact to the record the caller states, if its origin admits that writer.</summary>
    /// <param name="amendment">The record the contact is to have, and the origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The amended record, or the refusal naming what stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="amendment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the amendment does not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    public Task<ContactWriteResult> AmendAsync(ContactAmendment amendment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amendment);

        return this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var held = await this.directory.FindAsync(amendment.ContactId, token);

                if (held is null)
                {
                    return ContactWriteResult.NotFound();
                }

                if (!held.IsAmendableBy(amendment.Writer))
                {
                    return ContactWriteResult.OriginRefusesWriter(held);
                }

                var amended = held.AmendedWith(
                    amendment.DisplayName,
                    amendment.Addresses,
                    amendment.PreferredAddress,
                    amendment.Note,
                    this.timeProvider.GetUtcNow());

                if (await this.AddressHolderOtherThanAsync(amended, token) is { } holder)
                {
                    return ContactWriteResult.AddressHeldBy(holder);
                }

                return await this.store.ReplaceAsync(session, amended, token)
                    ? ContactWriteResult.Written(amended)
                    : ContactWriteResult.NotFound();
            },
            cancellationToken);
    }

    /// <summary>Promotes a collected contact to one the owner has taken responsibility for.</summary>
    /// <param name="contactId">The contact to promote.</param>
    /// <param name="writer">The origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The promoted record, or the refusal naming what stopped it.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <remarks>
    /// The one act that changes an origin, and it runs one way. It is gated on the writer for the reason an amendment is:
    /// promotion is the owner taking a record on, so collection asking for it is refused rather than granted the authority
    /// it was about to award itself. A contact that is already asserted is answered as such rather than written again, so
    /// an owner repeating the request learns that nothing was left to do.
    /// </remarks>
    public Task<ContactWriteResult> PromoteAsync(
        ContactId contactId,
        ContactOrigin writer,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var held = await this.directory.FindAsync(contactId, token);

                if (held is null)
                {
                    return ContactWriteResult.NotFound();
                }

                if (!held.IsPromotableBy(writer))
                {
                    return ContactWriteResult.OriginRefusesWriter(held);
                }

                if (held.Origin == ContactOrigin.Asserted)
                {
                    return ContactWriteResult.AlreadyAsserted(held);
                }

                var promoted = held.PromotedToAsserted(this.timeProvider.GetUtcNow());

                return await this.store.ReplaceAsync(session, promoted, token)
                    ? ContactWriteResult.Written(promoted)
                    : ContactWriteResult.NotFound();
            },
            cancellationToken);

    /// <summary>Erases one person and everything the book derived from them.</summary>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed.</returns>
    /// <remarks>
    /// Erasure is not a write a writer's origin gates. It is the data-subject path, and a person asking to be removed
    /// from somebody's contact book is not answered with which half of the book they happen to be in.
    /// </remarks>
    public Task<ContactErasure> EraseAsync(ContactId contactId, CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            (session, token) => this.store.EraseAsync(session, contactId, token),
            cancellationToken);

    /// <summary>Produces everything the book holds about one person.</summary>
    /// <param name="contactId">The contact to export.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export, or <see langword="null" /> when the book holds no such contact.</returns>
    public async Task<ContactExport?> ExportAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        var held = await this.directory.FindAsync(contactId, cancellationToken);

        return held is null ? null : new ContactExport(held, this.timeProvider.GetUtcNow());
    }

    /// <summary>Names the contact already holding one of this record's addresses, when it is a different contact.</summary>
    /// <remarks>
    /// The record's own identity is excluded, because an amendment keeping an address the contact already holds is not a
    /// clash with itself. The read joins no transaction, so it can be stale by the time the insert runs — which is what
    /// the unique constraint underneath is for, and why losing that race is retried rather than reported.
    /// </remarks>
    private async Task<ContactId?> AddressHolderOtherThanAsync(Contact contact, CancellationToken cancellationToken)
    {
        var holders = await this.directory.FindHoldersOfAsync(contact.Addresses, cancellationToken);
        var otherHolders = holders.Values.Where(holder => holder != contact.Id).ToArray();

        return otherHolders.Length == 0 ? null : otherHolders[0];
    }
}
