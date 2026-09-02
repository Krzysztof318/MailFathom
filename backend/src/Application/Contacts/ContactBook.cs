// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>The acts a contact book supports: read it — a page of it, one person, or whoever holds an address — record a person, amend one, promote one, erase one or the whole collected half, and export one.</summary>
/// <remarks>
/// <para>
/// Every surface over the book — the administration tool, the MCP tools, and collection from arriving mail — performs
/// these acts and no others, which is what keeps the origin rule from being a convention each of them remembers.
/// A writer names the origin it acts under, and a contact is amendable only by a writer of its own: collection never
/// touches what an owner wrote down, and an owner promotes a collected contact rather than editing it in place.
/// Promotion names the writer for the same reason, so the act of taking a record on is the owner's rather than something
/// collection can perform on its own behalf.
/// </para>
/// <para>
/// A book belongs to one owner, and every act resolves whose through <see cref="ContactBookOwnership" /> before it
/// reaches the store or the directory. So an identity, an address, or a name from another owner's book is answered as
/// one this book does not hold, and two people who correspond with the same person each keep their own record of them.
/// The origin rule above runs within a book rather than across the deployment, for the same reason.
/// </para>
/// <para>
/// Each write is idempotent from a fresh read and is committed through the optimistic concurrency policy, so two callers
/// claiming one address converge on the same answer instead of one of them meeting a provider failure: the loser's
/// insert violates the unique constraint over the owner and the address, the retry re-reads, and the second caller is
/// told which contact of their own book holds it.
/// </para>
/// <para>
/// Nothing here logs. A name, an address, and a note are personal data about a third party, and the outcomes this type
/// produces are what a surface reports; a log line about a write would put the whole book into a log within a week of
/// somebody using it.
/// </para>
/// <para>
/// Every act states the grant it is reached under, because a check that lived only in a route would be one a second
/// entrypoint forgets. Reading the book is <see cref="MailFathomPermission.AdminAuditRead" />, since a collected contact
/// is somebody this deployment learned about from correspondence rather than a report of its own state; writing one is
/// <see cref="MailFathomPermission.AdminOperate" />; and erasing a person is
/// <see cref="MailFathomPermission.AdminErase" />, beside the erasure of stored mail.
/// </para>
/// <para>
/// Two surfaces perform this book's writes and each publishes them under a name of its own, so recording, amending, and
/// erasing admit the administrative grant above <em>or</em> <see cref="MailFathomPermission.MailContactsWrite" />, which
/// is what an agent reaching the contact tools holds. The halves are disjoint, so requiring one name would leave the act
/// reachable from the operator and dead from the protocol. Promotion is written the same way and for the same reason: a
/// collected record exists to be taken on, and an agent that read the book has the same standing to do it as an operator
/// at a terminal. The alternative stops where the act does — exporting a person answers a data-subject request rather
/// than an agent's question, and erasing the whole collected half is an owner reversing a decision they made in
/// configuration.
/// </para>
/// <para>
/// Collection from arriving mail is work no caller requests, so the two acts it performs — asking whether an address is
/// spoken for, and recording somebody under the collected origin — admit MailFathom's own process identity instead of a
/// grant. A permission there would make writing into the collected origin reachable by whoever an operator granted that
/// name to, which is the authority the origin rule exists to keep away from a caller.
/// </para>
/// </remarks>
public sealed class ContactBook
{
    private readonly IContactStore store;
    private readonly IContactDirectory directory;
    private readonly ContactBookOwnership ownership;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the book over the store it writes to and the directory it reads from.</summary>
    /// <param name="store">Keeps the records.</param>
    /// <param name="directory">Answers what the book already holds.</param>
    /// <param name="ownership">Answers whose book each act reads and writes.</param>
    /// <param name="commitPolicy">Commits each write, retrying a lost race from a fresh read.</param>
    /// <param name="timeProvider">Stamps when a contact was recorded, amended, promoted, or exported.</param>
    /// <param name="authorization">Answers which principal reached each act.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBook(
        IContactStore store,
        IContactDirectory directory,
        ContactBookOwnership ownership,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.store = store;
        this.directory = directory;
        this.ownership = ownership;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
    }

    /// <summary>Reads one bounded page of the book.</summary>
    /// <param name="query">Which part of the book, how large a page, and where to continue from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <remarks>The page is bounded by the query the caller composed, which is where the ceiling on how much of a person's correspondents leaves the database at once already lives.</remarks>
    public Task<ContactPage> ReadPageAsync(ContactQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.directory.ReadPageAsync(this.ownership.Owner, query, cancellationToken);
    }

    /// <summary>Reads one contact by the identity the book gave it.</summary>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The contact, or <see langword="null" /> where the book holds no such person.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    public Task<Contact?> FindAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.directory.FindAsync(this.ownership.Owner, contactId, cancellationToken);
    }

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="address">The address to resolve, in its comparison form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The contact, or <see langword="null" /> where nobody in the book holds it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <remarks>Resolving an address to a person is the most pointed read the book answers, which is why it asks for the same grant the listing does rather than a weaker one.</remarks>
    public Task<Contact?> FindByAddressAsync(EmailAddress address, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.directory.FindByAddressAsync(this.ownership.Owner, address, cancellationToken);
    }

    /// <summary>Answers whether the book already holds one address, without answering whose it is.</summary>
    /// <param name="address">The address to look for, matched on its comparison form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when some contact holds it, whichever origin that contact has.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but MailFathom's own identity.</exception>
    /// <remarks>
    /// The read collection performs before it decides whether to record somebody, and it answers a question rather than
    /// producing a record deliberately: collection needs to know that an address is spoken for, and handing it the
    /// contact would put a person the owner asserted into the hands of work that may not touch them. It admits the
    /// process identity alone, for the reason <see cref="CollectAsync" /> does.
    /// </remarks>
    public async Task<bool> HoldsAddressAsync(EmailAddress address, CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        return await this.directory.FindByAddressAsync(this.ownership.Owner, address, cancellationToken) is not null;
    }

    /// <summary>Records a person collection inferred from arriving mail.</summary>
    /// <param name="newContact">The person to record, which collection states under its own origin.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record as written, or the contact that already holds one of its addresses.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="newContact" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the supplied record does not name the collected origin, or does not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but MailFathom's own identity.</exception>
    /// <remarks>
    /// <para>
    /// The act collection performs, and the one act on this book no caller can reach. It admits the process identity
    /// alone rather than a permission, because work nobody requested is what it is: a grant would make it reachable by
    /// whoever an operator granted that name to, and writing into the collected origin is precisely the authority the
    /// origin rule exists to keep away from a caller.
    /// </para>
    /// <para>
    /// It refuses a record naming any origin but <see cref="ContactOrigin.Collected" />, so the one writer that could
    /// award itself an owner's authority cannot do it by stating a different origin on the way in.
    /// </para>
    /// </remarks>
    public Task<ContactWriteResult> CollectAsync(NewContact newContact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newContact);

        this.authorization.RequireProcessIdentity();

        if (newContact.Origin != ContactOrigin.Collected)
        {
            throw new ArgumentException(
                "Collection records contacts under the collected origin and no other.",
                nameof(newContact));
        }

        return this.WriteNewContactAsync(newContact, cancellationToken);
    }

    /// <summary>Records a person the book does not yet hold.</summary>
    /// <param name="newContact">The person to record and the origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record as written, or the contact that already holds one of its addresses.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="newContact" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the supplied addresses do not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <remarks>
    /// The identity is minted here, from a UUID version 7 over the instant of the write, so the book's own identifiers
    /// order the way the records were created without a caller being able to choose one.
    /// </remarks>
    public Task<ContactWriteResult> RecordAsync(NewContact newContact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newContact);

        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite);

        return this.WriteNewContactAsync(newContact, cancellationToken);
    }

    /// <summary>Amends a contact to the record the caller states, if its origin admits that writer.</summary>
    /// <param name="amendment">The record the contact is to have, and the origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The amended record, or the refusal naming what stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="amendment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the amendment does not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <remarks>The grant is asked for before the book is read, so a caller who may not write cannot learn from the refusal whether the book holds the person they named.</remarks>
    public Task<ContactWriteResult> AmendAsync(ContactAmendment amendment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amendment);

        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite);

        var owner = this.ownership.Owner;

        return this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var held = await this.directory.FindAsync(owner, amendment.ContactId, token);

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

                if (await this.AddressHolderOtherThanAsync(owner, amended, token) is { } holder)
                {
                    return ContactWriteResult.AddressHeldBy(holder);
                }

                return await this.store.ReplaceAsync(session, owner, amended, token)
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
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" /> or <see cref="MailFathomPermission.MailContactsWrite" />.</exception>
    /// <remarks>
    /// The one act that changes an origin, and it runs one way. It is gated on the writer for the reason an amendment is:
    /// promotion is the owner taking a record on, so collection asking for it is refused rather than granted the authority
    /// it was about to award itself. A contact that is already asserted is answered as such rather than written again, so
    /// an owner repeating the request learns that nothing was left to do.
    /// <para>
    /// The writer and the grant answer different questions and both are asked: the grant says whether this caller may
    /// write to the book at all, and the writer says whether the record's own origin admits what it is about to do.
    /// </para>
    /// <para>
    /// Both surfaces reach it, because promotion is what a collected record is *for*: an agent that read the book and
    /// found somebody the deployment picked up has the same standing to take that record on as an operator at a
    /// terminal, and a promotion reachable from one of them would leave collection's own output editable from the other
    /// only by erasing it and writing it again. What the alternative does not widen is which writer may perform it —
    /// collection acts under its own origin and <see cref="Contact.IsPromotableBy" /> refuses it there.
    /// </para>
    /// </remarks>
    public Task<ContactWriteResult> PromoteAsync(
        ContactId contactId,
        ContactOrigin writer,
        CancellationToken cancellationToken)
    {
        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite);

        var owner = this.ownership.Owner;

        return this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var held = await this.directory.FindAsync(owner, contactId, token);

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

                return await this.store.ReplaceAsync(session, owner, promoted, token)
                    ? ContactWriteResult.Written(promoted)
                    : ContactWriteResult.NotFound();
            },
            cancellationToken);
    }

    /// <summary>Erases one person and everything the book derived from them.</summary>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the erasure was reached by anything but a caller granted <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// Erasure is not a write a writer's origin gates. It is the data-subject path, and a person asking to be removed
    /// from somebody's contact book is not answered with which half of the book they happen to be in.
    /// <para>
    /// It asks for the erasing grant rather than the writing one, beside the erasure of stored mail, because what it
    /// destroys cannot be written back and a credential provisioned to correct a record should not be able to remove one.
    /// </para>
    /// </remarks>
    public Task<ContactErasure> EraseAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminErase,
            MailFathomPermission.MailContactsWrite);

        var owner = this.ownership.Owner;

        return this.commitPolicy.CommitAsync(
            (session, token) => this.store.EraseAsync(session, owner, contactId, token),
            cancellationToken);
    }

    /// <summary>Erases every contact this deployment collected, leaving the ones the owner asserted where they are.</summary>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the erasure was reached by anything but a caller granted <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// The answer to an owner who changed their mind about collection. Everything collection produced is a contact of
    /// its own origin — it keeps no ledger, and the evidence it reads is the mail that was already there — so taking
    /// that origin out is taking out the whole of what it built, and nothing of what the owner entered goes with it.
    /// </para>
    /// <para>
    /// It stays the operator's act under the erasing grant, beside the erasure of one person and of stored mail, and is
    /// not one of the acts the contact tools publish: switching collection off is a configuration change and undoing its
    /// output is a disposal, and neither is something an agent should be able to do on somebody's behalf.
    /// </para>
    /// </remarks>
    public Task<CollectedContactErasure> EraseCollectedAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminErase);

        var owner = this.ownership.Owner;

        return this.commitPolicy.CommitAsync(
            (session, token) => this.store.EraseCollectedAsync(session, owner, token),
            cancellationToken);
    }

    /// <summary>Produces everything the book holds about one person.</summary>
    /// <param name="contactId">The contact to export.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export, or <see langword="null" /> when the book holds no such contact.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the export was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <remarks>It is what a data-subject access request is answered from, which is reading what this deployment derived about a person rather than a report of its own state.</remarks>
    public async Task<ContactExport?> ExportAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        var held = await this.directory.FindAsync(this.ownership.Owner, contactId, cancellationToken);

        return held is null ? null : new ContactExport(held, this.timeProvider.GetUtcNow());
    }

    /// <summary>Mints the identity for a person nobody has written down yet and stages the record, whoever asked.</summary>
    /// <remarks>
    /// Shared by the two acts that add a contact, because who may add one and what adding one does are different
    /// questions and only the first of them differs between a caller and collection. The identity is a UUID version 7
    /// over the instant of the write, so the book's own identifiers order the way the records were created without a
    /// caller being able to choose one.
    /// </remarks>
    private Task<ContactWriteResult> WriteNewContactAsync(NewContact newContact, CancellationToken cancellationToken)
    {
        var owner = this.ownership.Owner;
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
                if (await this.AddressHolderOtherThanAsync(owner, contact, token) is { } holder)
                {
                    return ContactWriteResult.AddressHeldBy(holder);
                }

                await this.store.AddAsync(session, owner, contact, token);

                return ContactWriteResult.Written(contact);
            },
            cancellationToken);
    }

    /// <summary>Names one contact already holding one of this record's addresses, when it is a different contact.</summary>
    /// <remarks>
    /// The record's own identity is excluded, because an amendment keeping an address the contact already holds is not a
    /// clash with itself. Where several other contacts hold addresses this record claims, one of them is named and
    /// <see cref="ContactWriteResult.AddressHolder" /> states why. The read joins no transaction, so it can be stale by
    /// the time the insert runs — which is what the unique constraint underneath is for, and why losing that race is
    /// retried rather than reported.
    /// </remarks>
    private async Task<ContactId?> AddressHolderOtherThanAsync(
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken)
    {
        var holders = await this.directory.FindHoldersOfAsync(owner, contact.Addresses, cancellationToken);
        var otherHolders = holders.Values.Where(holder => holder != contact.Id).ToArray();

        return otherHolders.Length == 0 ? null : otherHolders[0];
    }
}
