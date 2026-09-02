// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>States who one account's mail is sent as, which is the account's own decision and never a caller's.</summary>
/// <remarks>
/// It is resolved from configuration for the account a send names, so the only way to send as a different person is to
/// send through a different account — which is a decision an operator makes in a file rather than one any request can
/// reach. The display name travels with the address for the same reason: both are what recipients see this mailbox as,
/// and letting a caller supply either would be letting it write as somebody else in the half that recipients read
/// first.
/// </remarks>
public sealed record OutgoingSenderIdentity
{
    private OutgoingSenderIdentity(MailAccountId accountId, EmailAddress address, string domain)
    {
        this.AccountId = accountId;
        this.Address = address;
        this.Domain = domain;
    }

    /// <summary>Gets the account this identity belongs to.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the address every message this account sends is written from, with the name written beside it.</summary>
    public EmailAddress Address { get; }

    /// <summary>Gets the mail domain of the sending address, which is what a minted message identity is unique within.</summary>
    public string Domain { get; }

    /// <summary>Names who one account sends as.</summary>
    /// <param name="accountId">The account the identity belongs to.</param>
    /// <param name="address">The address its mail is written from.</param>
    /// <returns>The identity those two name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="address" /> names no mailbox.</exception>
    /// <remarks>
    /// The address is checked rather than assumed, because a default <see cref="EmailAddress" /> carries none at all and
    /// would compose a message written from nobody. What guarantees the domain exists is the same check: an address
    /// this type accepts has one.
    /// </remarks>
    public static OutgoingSenderIdentity Create(MailAccountId accountId, EmailAddress address)
    {
        if (string.IsNullOrEmpty(address.Address))
        {
            throw new ArgumentException("A sending identity names the mailbox its mail is written from.", nameof(address));
        }

        return new OutgoingSenderIdentity(
            accountId,
            address,
            address.Address[(address.Address.LastIndexOf('@') + 1)..]);
    }
}
