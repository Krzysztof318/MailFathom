// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>The acts a contact book supports: read it — a page of it, one person, or whoever holds an address — record a person, amend one, promote one, erase one or a mailbox's whole collected book, and export one.</summary>
/// <remarks>
/// <para>
/// Every surface over the books — the administration tool, the MCP tools, and collection from arriving mail — performs
/// these acts and no others, which is what keeps the origin rule from being a convention each of them remembers.
/// A writer names the origin it acts under, and a contact is amendable only by a writer of its own: collection never
/// touches what a user wrote down, and a user promotes a collected contact rather than editing it in place.
/// Promotion names the writer for the same reason, so the act of taking a record on is the user's rather than something
/// collection can perform on its own behalf.
/// </para>
/// <para>
/// <b>There are two kinds of book and each act says which it acts on.</b> A user holds the people they wrote down; a
/// mail account holds the people its mail says it corresponds with, once, however many users are assigned that
/// mailbox. So a read is stated over a <see cref="ContactBookScope" /> — one user's own book and the collected book of
/// each account they are assigned — and a write is stated over the single book it lands in. Nothing here derives
/// either from the deployment: an act that could not say whose book it meant is an act that would have to guess, and
/// guessing is how one person is handed another person's correspondents.
/// </para>
/// <para>
/// Each write is idempotent from a fresh read and is committed through the optimistic concurrency policy, so two callers
/// claiming one address converge on the same answer instead of one of them meeting a provider failure: the loser's
/// insert violates the unique constraint over the book and the address, the retry re-reads, and the second caller is
/// told which contact of that book holds it.
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
/// than an agent's question, and erasing a mailbox's whole collected book is a user reversing a decision they made in
/// configuration.
/// </para>
/// <para>
/// Collection from arriving mail is work no caller requests, so the two acts it performs — asking whether an address is
/// spoken for in the account's own book, and recording somebody in it — admit MailFathom's own process identity instead
/// of a grant. A permission there would make writing into a collected book reachable by whoever an operator granted
/// that name to, which is the authority the origin rule exists to keep away from a caller.
/// </para>
/// </remarks>
public sealed class ContactBook
{
    private readonly IContactStore store;
    private readonly IContactDirectory directory;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the book over the store it writes to and the directory it reads from.</summary>
    /// <param name="store">Keeps the records.</param>
    /// <param name="directory">Answers what the books already hold.</param>
    /// <param name="commitPolicy">Commits each write, retrying a lost race from a fresh read.</param>
    /// <param name="timeProvider">Stamps when a contact was recorded, amended, promoted, or exported.</param>
    /// <param name="authorization">Answers which principal reached each act.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBook(
        IContactStore store,
        IContactDirectory directory,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.store = store;
        this.directory = directory;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
    }

    /// <summary>Reads one bounded page of the books a scope reads.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="query">Which part of the books, how large a page, and where to continue from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <remarks>The page is bounded by the query the caller composed, which is where the ceiling on how much of a person's correspondents leaves the database at once already lives.</remarks>
    public Task<ContactPage> ReadPageAsync(
        ContactBookScope scope,
        ContactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.directory.ReadPageAsync(scope, query, cancellationToken);
    }

    /// <summary>Reads one contact by the identity the book gave it.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The contact, or <see langword="null" /> where the scope shows no such person.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    public Task<Contact?> FindAsync(
        ContactBookScope scope,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.directory.FindAsync(scope, contactId, cancellationToken);
    }

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="address">The address to resolve, in its comparison form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The contact, or <see langword="null" /> where nobody in the scope holds it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <remarks>Resolving an address to a person is the most pointed read the book answers, which is why it asks for the same grant the listing does rather than a weaker one.</remarks>
    public Task<Contact?> FindByAddressAsync(
        ContactBookScope scope,
        EmailAddress address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.directory.FindByAddressAsync(scope, address, cancellationToken);
    }

    /// <summary>Answers whether one account's own book already holds an address, without answering whose it is.</summary>
    /// <param name="account">The account whose book is asked about.</param>
    /// <param name="address">The address to look for, matched on its comparison form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when some contact of that account's book holds it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the read was reached by anything but MailFathom's own identity.</exception>
    /// <remarks>
    /// The read collection performs before it decides whether to record somebody, and it answers a question rather than
    /// producing a record deliberately: collection needs to know that an address is spoken for, and handing it the
    /// contact would put a person into the hands of work that may not touch them. It asks about the account's own book
    /// alone, because that is the book it would be writing into — what a user of that mailbox happens to have written
    /// down in their own book is theirs, and is answered for at the read instead. It admits the process identity alone,
    /// for the reason <see cref="CollectAsync" /> does.
    /// </remarks>
    public async Task<bool> HoldsAddressAsync(
        MailAccountId account,
        EmailAddress address,
        CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        var held = await this.directory.FindHoldersOfAsync(
            ContactBookHolder.Of(account),
            [address],
            cancellationToken);

        return held.Count > 0;
    }

    /// <summary>Records a person collection inferred from arriving mail, in the book of the account it arrived on.</summary>
    /// <param name="account">The account being synchronized, whose book the record goes into.</param>
    /// <param name="newContact">The person to record, which collection states under its own origin.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record as written, or the contact of that account's book that already holds one of its addresses.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="newContact" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the supplied record does not name the collected origin, or does not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but MailFathom's own identity.</exception>
    /// <remarks>
    /// <para>
    /// The act collection performs, and the one act on these books no caller can reach. It admits the process identity
    /// alone rather than a permission, because work nobody requested is what it is: a grant would make it reachable by
    /// whoever an operator granted that name to, and writing into a collected book is precisely the authority the
    /// origin rule exists to keep away from a caller.
    /// </para>
    /// <para>
    /// It refuses a record naming any origin but <see cref="ContactOrigin.Collected" />, so the one writer that could
    /// award itself a user's authority cannot do it by stating a different origin on the way in.
    /// </para>
    /// </remarks>
    public Task<ContactWriteResult> CollectAsync(
        MailAccountId account,
        NewContact newContact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newContact);

        this.authorization.RequireProcessIdentity();

        if (newContact.Origin != ContactOrigin.Collected)
        {
            throw new ArgumentException(
                "Collection records contacts under the collected origin and no other.",
                nameof(newContact));
        }

        return this.WriteNewContactAsync(ContactBookHolder.Of(account), newContact, cancellationToken);
    }

    /// <summary>Records a person one user's own book does not yet hold.</summary>
    /// <param name="user">The user whose book the person is written into.</param>
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
    public Task<ContactWriteResult> RecordAsync(
        UserId user,
        NewContact newContact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newContact);

        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite);

        return this.WriteNewContactAsync(ContactBookHolder.Of(user), newContact, cancellationToken);
    }

    /// <summary>Amends a contact of the caller's own book to the record they state, if its origin admits that writer.</summary>
    /// <param name="scope">The books the caller reads, whose own book the amendment may reach.</param>
    /// <param name="amendment">The record the contact is to have, and the origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The amended record, or the refusal naming what stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="amendment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the amendment does not form a contact the domain admits.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <remarks>
    /// It reads the whole scope and writes to the own book alone. Reading the whole of it is what lets a collected
    /// record be refused as one rather than as a record nobody holds: it is a mailbox's, several users may be reading
    /// it, and rewriting it in place would change what each of them sees — so the origin rule refuses the writer and
    /// <see cref="PromoteAsync" /> is the act that makes a copy the user may amend. A contact outside the scope
    /// altogether is a contact this caller does not hold, and is answered as such.
    /// <para>
    /// The grant is asked for before the book is read, so a caller who may not write cannot learn from the refusal
    /// whether the book holds the person they named.
    /// </para>
    /// </remarks>
    public Task<ContactWriteResult> AmendAsync(
        ContactBookScope scope,
        ContactAmendment amendment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(amendment);

        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite);

        var holder = scope.OwnBook;

        return this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var held = await this.directory.FindAsync(scope, amendment.ContactId, token);

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

                if (await this.AddressHolderOtherThanAsync(holder, amended, token) is { } addressHolder)
                {
                    return ContactWriteResult.AddressHeldBy(addressHolder);
                }

                return await this.store.ReplaceAsync(session, holder, amended, token)
                    ? ContactWriteResult.Written(amended)
                    : ContactWriteResult.NotFound();
            },
            cancellationToken);
    }

    /// <summary>Takes a collected contact on, by writing the user's own copy of it.</summary>
    /// <param name="scope">The books the caller reads, whose own book the copy is written into.</param>
    /// <param name="contactId">The contact to promote.</param>
    /// <param name="writer">The origin the writer acts under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The promoted record, or the refusal naming what stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed attempt lost the race.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the write was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" /> or <see cref="MailFathomPermission.MailContactsWrite" />.</exception>
    /// <remarks>
    /// <para>
    /// <b>It copies rather than moves</b>, because the record it is promoting is the mailbox's and the mailbox may be
    /// somebody else's too. So the collected record stays where it is, for whoever else is assigned that account, and
    /// what the caller gains is a record of their own — with a new identity, because it is a new record in a different
    /// book — which the read then shows in place of the collected one. A user who wants the collected record gone as
    /// well erases it, which is the act that reaches the mailbox's book.
    /// </para>
    /// <para>
    /// It is gated on the writer for the reason an amendment is: promotion is the user taking a record on, so collection
    /// asking for it is refused rather than granted the authority it was about to award itself. A contact that is
    /// already the caller's own is answered as such rather than written again, so a user repeating the request learns
    /// that nothing was left to do.
    /// </para>
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
        ContactBookScope scope,
        ContactId contactId,
        ContactOrigin writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminOperate,
            MailFathomPermission.MailContactsWrite);

        return this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var held = await this.directory.FindAsync(scope, contactId, token);

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

                return await this.StageNewContactAsync(
                    session,
                    scope.OwnBook,
                    new NewContact
                    {
                        DisplayName = held.DisplayName,
                        Addresses = [.. held.Addresses],
                        PreferredAddress = held.PreferredAddress,
                        Note = held.Note,
                        Origin = ContactOrigin.Asserted,
                    },
                    token);
            },
            cancellationToken);
    }

    /// <summary>Erases one person and everything the book derived from them.</summary>
    /// <param name="scope">The books the erasure may reach.</param>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the erasure was reached by anything but a caller granted <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// Erasure is not a write a writer's origin gates. It is the data-subject path, and a person asking to be removed
    /// from somebody's contact book is not answered with which book they happen to be in — so it reaches the whole
    /// scope, a collected record included, and a collected record erased is gone for every user assigned that mailbox
    /// because the record was one record rather than a copy each.
    /// <para>
    /// It asks for the erasing grant rather than the writing one, beside the erasure of stored mail, because what it
    /// destroys cannot be written back and a credential provisioned to correct a record should not be able to remove one.
    /// </para>
    /// </remarks>
    public Task<ContactErasure> EraseAsync(
        ContactBookScope scope,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequireAnyPermission(
            MailFathomPermission.AdminErase,
            MailFathomPermission.MailContactsWrite);

        return this.commitPolicy.CommitAsync(
            (session, token) => this.store.EraseAsync(session, scope, contactId, token),
            cancellationToken);
    }

    /// <summary>Erases the whole of one mail account's collected book, leaving every user's own book where it is.</summary>
    /// <param name="account">The account whose book is erased.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What the erasure removed.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the erasure was reached by anything but a caller granted <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// The answer to a user who changed their mind about collection. Everything collection produced for one mailbox is
    /// in that mailbox's book — it keeps no ledger, and the evidence it reads is the mail that was already there — so
    /// emptying the book is taking out the whole of what it built there, and nothing anybody wrote down goes with it.
    /// It is stated per account because collection is switched on per account: emptying one mailbox's book is not
    /// emptying another's.
    /// </para>
    /// <para>
    /// It stays the operator's act under the erasing grant, beside the erasure of one person and of stored mail, and is
    /// not one of the acts the contact tools publish: switching collection off is a configuration change and undoing its
    /// output is a disposal, and neither is something an agent should be able to do on somebody's behalf.
    /// </para>
    /// </remarks>
    public Task<CollectedContactErasure> EraseCollectedAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminErase);

        return this.commitPolicy.CommitAsync(
            (session, token) => this.store.EraseCollectedAsync(session, account, token),
            cancellationToken);
    }

    /// <summary>Produces everything the books hold about one person.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="contactId">The contact to export.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The export, or <see langword="null" /> when the scope shows no such contact.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the export was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <remarks>It is what a data-subject access request is answered from, which is reading what this deployment derived about a person rather than a report of its own state.</remarks>
    public async Task<ContactExport?> ExportAsync(
        ContactBookScope scope,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        var held = await this.directory.FindAsync(scope, contactId, cancellationToken);

        return held is null ? null : new ContactExport(held, this.timeProvider.GetUtcNow());
    }

    /// <summary>Mints the identity for a person nobody has written down yet and commits the record, whoever asked.</summary>
    /// <remarks>
    /// Shared by the two acts that add a contact, because who may add one and what adding one does are different
    /// questions and only the first of them differs between a caller and collection.
    /// </remarks>
    private Task<ContactWriteResult> WriteNewContactAsync(
        ContactBookHolder holder,
        NewContact newContact,
        CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            (session, token) => this.StageNewContactAsync(session, holder, newContact, token),
            cancellationToken);

    /// <summary>Stages one new record into one book, inside a commit somebody else opened.</summary>
    /// <remarks>
    /// Separate from the commit above because promotion reads before it writes and has to do both inside one attempt:
    /// a retry re-reads the collected record it is copying rather than writing the copy it staged the first time. The
    /// identity is a UUID version 7 over the instant of the write, so the book's own identifiers order the way the
    /// records were created without a caller being able to choose one.
    /// </remarks>
    private async Task<ContactWriteResult> StageNewContactAsync(
        IPersistenceSession session,
        ContactBookHolder holder,
        NewContact newContact,
        CancellationToken cancellationToken)
    {
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

        if (await this.AddressHolderOtherThanAsync(holder, contact, cancellationToken) is { } addressHolder)
        {
            return ContactWriteResult.AddressHeldBy(addressHolder);
        }

        await this.store.AddAsync(session, holder, contact, cancellationToken);

        return ContactWriteResult.Written(contact);
    }

    /// <summary>Names one contact of the same book already holding one of this record's addresses, when it is a different contact.</summary>
    /// <remarks>
    /// The record's own identity is excluded, because an amendment keeping an address the contact already holds is not a
    /// clash with itself. Where several other contacts hold addresses this record claims, one of them is named and
    /// <see cref="ContactWriteResult.AddressHolder" /> states why. The read joins no transaction, so it can be stale by
    /// the time the insert runs — which is what the unique constraint underneath is for, and why losing that race is
    /// retried rather than reported.
    /// </remarks>
    private async Task<ContactId?> AddressHolderOtherThanAsync(
        ContactBookHolder holder,
        Contact contact,
        CancellationToken cancellationToken)
    {
        var holders = await this.directory.FindHoldersOfAsync(holder, contact.Addresses, cancellationToken);
        var otherHolders = holders.Values.Where(held => held != contact.Id).ToArray();

        return otherHolders.Length == 0 ? null : otherHolders[0];
    }
}
