// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Accounts;

/// <summary>Names one mail account in full: the owner it belongs to and the identifier its operator chose.</summary>
/// <remarks>
/// <para>
/// <see cref="MailAccountId" /> is the identifier a person wrote, and it names one account within its owner rather than
/// across the deployment. This is the whole of what identifies an account, and it is what every write that records an
/// account reference names it by — so a stored row can say whose account it belongs to without a join, and a value
/// travelling between an owner-scoped resolution and a write cannot lose the half that says whose.
/// </para>
/// <para>
/// A read names an account through <c>MailboxScope</c> instead, which carries the owner once beside the accounts it
/// narrows to. The two shapes exist because a read narrows to a set of one owner's accounts while a write records
/// exactly one, and folding either into the other would put an owner on every element of a list that shares one.
/// </para>
/// <para>
/// It is deliberately not a published identity: nothing serializes it, no tool argument carries it, and no failure
/// names it. What a caller writes and reads back is the identifier alone, resolved within the owner they are acting
/// for.
/// </para>
/// </remarks>
public readonly record struct MailAccountIdentity
{
    private MailAccountIdentity(MailOwnerId owner, MailAccountId id)
    {
        this.Owner = owner;
        this.Id = id;
    }

    /// <summary>Gets the owner the account belongs to.</summary>
    public MailOwnerId Owner { get; }

    /// <summary>Gets the identifier the account is named by within its owner.</summary>
    public MailAccountId Id { get; }

    /// <summary>Creates the full identity of one mail account.</summary>
    /// <param name="owner">The owner the account belongs to.</param>
    /// <param name="id">The identifier the account is named by within that owner.</param>
    /// <returns>The account identity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, which is not an account this deployment can record anything about.</exception>
    /// <remarks>
    /// The owner is required to be a named one, because a row written under the unspecified identity would be a row
    /// belonging to nobody — unreachable by any read and uncollected by any erasure. A caller that has no owner is
    /// refused before it reaches a write rather than being allowed to record one.
    /// </remarks>
    public static MailAccountIdentity Create(MailOwnerId owner, MailAccountId id)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A mail account belongs to a named owner, so an account identity is never composed from the unspecified one.",
                nameof(owner));
        }

        return new MailAccountIdentity(owner, id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The owner comes first because that is the order every index and every predicate reads the pair in. It is a
    /// diagnostic rendering rather than a published one: nothing parses it back, and the owner identity is generated
    /// and names nobody outside this deployment.
    /// </remarks>
    public override string ToString() => $"{this.Owner}/{this.Id}";
}
