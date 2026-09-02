// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Contacts.Collection;

/// <summary>Decides which of the addresses one account's mail carries collection may ever record.</summary>
/// <remarks>
/// <para>
/// The policy is the whole of what an account is willing to have written into the collected origin, and it answers two
/// questions rather than one. A message may say of itself that no person wrote it, in which case none of its addresses
/// is considered at all; and an address may name something other than a person to correspond with, in which case it is
/// not considered whatever message carried it. Both are refusals rather than scores, so an address the policy declines
/// is never recorded by any later reading.
/// </para>
/// <para>
/// Three rules refuse an address, and they are deliberately of different kinds.
/// <see cref="AutomatedMailboxName" /> is structural and every deployment gets it, because it names what nobody
/// corresponds with. The <see cref="Exclusions" /> are the owner's, because only they know which of their real
/// correspondents they would rather not have written down. And the deployment's own mailbox addresses are refused
/// because a contact book holding its owner is a book that answers "who is this from" with the person asking.
/// </para>
/// <para>
/// Everything the policy holds is personal data: an exclusion names somebody, and an own address names the owner.
/// Nothing here may be logged, and the policy carries no identity of its own for a log line to name instead.
/// </para>
/// </remarks>
public sealed class ContactCollectionPolicy
{
    /// <summary>A policy narrowing nothing, which is what an account that never reaches collection at all carries.</summary>
    /// <remarks>
    /// It admits every ordinary address, and that is safe rather than permissive: an account carrying it has collection
    /// switched off, so nothing ever asks it. What it is for is keeping the settings of such an account a whole value
    /// rather than one with a hole in it.
    /// </remarks>
    public static readonly ContactCollectionPolicy NothingExcluded = new([], new HashSet<EmailAddress>());
    private readonly IReadOnlySet<EmailAddress> ownAddresses;

    private ContactCollectionPolicy(
        IReadOnlyList<ContactCollectionExclusion> exclusions,
        IReadOnlySet<EmailAddress> ownAddresses)
    {
        this.Exclusions = exclusions;
        this.ownAddresses = ownAddresses;
    }

    /// <summary>Gets what the owner excluded, in the order they wrote it.</summary>
    public IReadOnlyList<ContactCollectionExclusion> Exclusions { get; }

    /// <summary>Builds the policy one account collects under.</summary>
    /// <param name="exclusions">What the owner excluded by domain or by pattern.</param>
    /// <param name="ownAddresses">The mailboxes this deployment reads on its owner's behalf.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The own addresses are a set rather than a list because they are matched on every address of every message, and
    /// they are the deployment's rather than the account's: an owner writing from one of their accounts to another is
    /// not a correspondent of themselves.
    /// </remarks>
    public static ContactCollectionPolicy Create(
        IReadOnlyList<ContactCollectionExclusion> exclusions,
        IReadOnlyCollection<EmailAddress> ownAddresses)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        ArgumentNullException.ThrowIfNull(ownAddresses);

        return new ContactCollectionPolicy(exclusions, ownAddresses.ToHashSet());
    }

    /// <summary>Answers whether a message carrying this claim about itself contributes any address at all.</summary>
    /// <param name="automation">What the message said about having been sent by a machine.</param>
    /// <returns><see langword="true" /> when the message is one a person wrote to a person.</returns>
    /// <remarks>
    /// Every claim refuses the whole message rather than one of its addresses, because what a list posting or an
    /// automatic reply establishes is not that one mailbox is a machine but that this message is not correspondence. A
    /// list posting in particular carries the author's own real address, which is exactly the address a rule about
    /// mailbox names could never catch.
    /// </remarks>
    public bool Admits(EmailAutomation automation) => automation == EmailAutomation.None;

    /// <summary>Answers whether one address may be recorded as somebody the owner corresponds with.</summary>
    /// <param name="address">The address a message carried.</param>
    /// <returns><see langword="true" /> when nothing refuses it.</returns>
    public bool Admits(EmailAddress address) =>
        !this.ownAddresses.Contains(address)
        && !AutomatedMailboxName.Names(address)
        && !this.Exclusions.Any(exclusion => exclusion.Excludes(address));
}
