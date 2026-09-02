// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>One mailbox or one organization an operator named on a recipient policy.</summary>
/// <remarks>
/// <para>
/// An entry names either a whole domain or a single address, and the two are different claims. A domain entry describes
/// a counterparty an instance corresponds with as an organization, which is the shape an operator restricting their
/// instance to their own team writes; an address entry narrows that to one mailbox, which is what a single
/// correspondent at a provider everybody else uses too needs.
/// </para>
/// <para>
/// A domain entry reaches the names beneath it as well as the domain itself, on both lists and without an opt-in. That
/// is one rule rather than two because the two lists are read together and the reading has to stay legible: on the
/// denied list it is the stricter answer outright, and on the allowed list it is what an operator writing their own
/// organization down means, since a mailbox at a department's subdomain is that organization's mailbox.
/// </para>
/// <para>
/// A recipient is somebody who is not this mailbox's owner, so an entry is personal data of theirs and reaches no log
/// line, metric dimension, span attribute, or exception message.
/// </para>
/// </remarks>
public sealed record OutgoingRecipientRule
{
    private OutgoingRecipientRule(SenderDomain domain, string? normalizedLocalPart)
    {
        this.Domain = domain;
        this.NormalizedLocalPart = normalizedLocalPart;
    }

    /// <summary>Gets the domain this entry names, which for an address entry is that address's domain.</summary>
    public SenderDomain Domain { get; }

    /// <summary>Gets the comparison form of the address's local part, or <see langword="null" /> for a domain entry.</summary>
    public string? NormalizedLocalPart { get; }

    /// <summary>Builds an entry naming a whole organization.</summary>
    /// <param name="domain">The domain text an operator wrote.</param>
    /// <param name="rule">The entry, when the text is a domain this system compares on.</param>
    /// <returns><see langword="true" /> when the text is usable; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The domain is held in the same comparison form every other domain in this system is, so an entry written in one
    /// encoding of an internationalized name matches an address that carried the other.
    /// </remarks>
    public static bool TryCreateForDomain(string? domain, [NotNullWhen(true)] out OutgoingRecipientRule? rule)
    {
        rule = null;

        if (!SenderDomain.TryCreate(domain, out var namedDomain))
        {
            return false;
        }

        rule = new OutgoingRecipientRule(namedDomain, normalizedLocalPart: null);

        return true;
    }

    /// <summary>Builds an entry naming one mailbox.</summary>
    /// <param name="address">The address text an operator wrote.</param>
    /// <param name="rule">The entry, when the text is an address this system compares on.</param>
    /// <returns><see langword="true" /> when the text is usable; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The address is split at the last at-sign, the way every address in this system is, because a quoted local part
    /// may contain one.
    /// </remarks>
    public static bool TryCreateForAddress(string? address, [NotNullWhen(true)] out OutgoingRecipientRule? rule)
    {
        rule = null;

        if (!EmailAddress.TryCreate(displayName: null, address, out var mailbox)
            || !mailbox.TrySplit(out var localPart, out var domainText)
            || !SenderDomain.TryCreate(domainText.ToString(), out var domain))
        {
            return false;
        }

        rule = new OutgoingRecipientRule(domain, localPart);

        return true;
    }

    /// <summary>Answers whether this entry names one recipient of an outgoing message.</summary>
    /// <param name="recipient">The address the message would be offered to.</param>
    /// <returns><see langword="true" /> when the entry names that address.</returns>
    /// <remarks>
    /// An address whose halves cannot be read matches nothing, which is the answer that keeps the denied list from
    /// being escaped by an address this system could not split and keeps the allowed list from admitting one.
    /// </remarks>
    public bool Matches(EmailAddress recipient)
    {
        if (!recipient.TrySplit(out var localPart, out var domainText)
            || !SenderDomain.TryCreate(domainText.ToString(), out var domain))
        {
            return false;
        }

        if (this.NormalizedLocalPart is { } namedLocalPart)
        {
            return domain == this.Domain && string.Equals(localPart, namedLocalPart, StringComparison.Ordinal);
        }

        return domain == this.Domain || domain.IsSubdomainOf(this.Domain);
    }
}
