// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One person an owner's contact book holds, with the addresses they use as rows of their own.</summary>
/// <remarks>
/// <para>
/// The book is one owner's, so every row names the owner whose it is and every read of it leads with that column. It
/// is the discriminator the mail graph does not need — mail reaches an owner through its account, and a contact hangs
/// on nothing that could carry one — which is why the column is here rather than derived.
/// </para>
/// <para>
/// The addresses are an association rather than an array column, which is what makes both obligations over them
/// structural: one of them can be unique across the whole book, and erasing the person takes them with it through the
/// foreign key instead of through a second statement somebody has to remember to write.
/// </para>
/// <para>
/// Every column but the identity and the origin holds personal data about a third party. It is held under the same
/// terms as the mail beside it — the same database, the same access, the same retention, and no field encryption, which
/// the deployment's own storage protection covers for both — and erasing it is a delete rather than a mark.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactEntity
{
    /// <summary>The longest name stored, which is the bound the domain value already refuses to exceed.</summary>
    internal const int MaximumDisplayNameLength = ContactDisplayName.MaximumLength;

    /// <summary>The longest note stored, which is the bound the domain value already refuses to exceed.</summary>
    internal const int MaximumNoteLength = ContactNote.MaximumLength;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the owner whose book holds this person.</summary>
    /// <remarks>
    /// Keyed onto the owner record, so erasing an owner takes their whole book with it rather than leaving the people
    /// they wrote down behind, and it never changes: a contact is not moved between books, it is written in one.
    /// </remarks>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the name as the owner wrote it, which is what a reader is shown.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Gets or sets the comparison form a listing is ordered and paginated by.</summary>
    /// <remarks>
    /// Stored rather than derived in the query, so the order a page is served in is decided by one rule in the domain
    /// instead of by the collation of a database MailFathom does not control, and so an index can be built over it.
    /// </remarks>
    public required string DisplayNameSortKey { get; set; }

    /// <summary>Gets or sets the comparison form of the address to use when something addresses this person without naming which.</summary>
    /// <remarks>
    /// <para>
    /// The choice is a column on the person rather than a flag on each address, which is what makes changing it one
    /// update instead of two that pass through a state where nobody, or everybody, is preferred. A flag would need a
    /// filtered unique index to hold the same rule, and that index refuses the intermediate row the second update was on
    /// its way to fixing.
    /// </para>
    /// <para>
    /// It is deliberately not a foreign key onto the address row. The two tables already point one way, and a key
    /// pointing back would make inserting either of them first impossible without deferring the constraint. That the
    /// named address is one the contact holds is the domain's invariant, enforced again by the mapping, which refuses a
    /// row naming an address the contact does not hold rather than picking one.
    /// </para>
    /// </remarks>
    public required string PreferredNormalizedAddress { get; set; }

    /// <summary>Gets or sets what the owner wrote about this person, or <see langword="null" /> when they wrote nothing.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets how this contact came to be in the book, which decides who may amend it.</summary>
    public ContactOrigin Origin { get; set; }

    /// <summary>Gets or sets when this contact entered the book.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Gets or sets when this contact was last amended.</summary>
    public DateTimeOffset AmendedAt { get; set; }

    /// <summary>Gets or sets PostgreSQL's own row version, which is what a write over a row somebody else moved fails on.</summary>
    /// <remarks>
    /// <para>
    /// A contact is amended in place, so it is exactly the mutable record
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
    /// requires a token on.
    /// </para>
    /// <para>
    /// What it settles is a row that changed or disappeared between the tracked read an amendment is applied to and the
    /// commit that writes it. The case worth naming is the second: a contact erased while an amendment was in flight is
    /// a write that affects no row, which the token turns into a conflict, so the retry re-reads and answers that the
    /// book holds nobody rather than putting the person back.
    /// </para>
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }

    /// <summary>Gets the addresses this person uses.</summary>
    public ICollection<ContactAddressEntity> Addresses { get; } = [];
}
