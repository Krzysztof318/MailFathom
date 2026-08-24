// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Answers how many of the addresses a caller wrote down nothing this deployment holds can vouch for.</summary>
/// <remarks>
/// <para>
/// Two things vouch for an address, and both are the owner's rather than any caller's. The contact book holds the
/// people this mailbox corresponds with — whether the owner wrote them down or collection recorded them from mail that
/// arrived — and the deployment's own accounts hold the mailboxes it reads on their behalf. An address in neither is one
/// this installation has no trace of at all, which is what the address inside an injected instruction looks like: a
/// message telling an agent to write to its author's accomplice is naming somebody the mailbox has never heard of.
/// </para>
/// <para>
/// It is asked only about addresses a caller supplied as text. A recipient resolved out of the book is vouched for by
/// the lookup that produced it, and one the answered message's own headers named is this system's reading of mail it
/// already holds — neither is a caller's word, and asking about either would refuse a plain reply on an installation
/// whose book is empty.
/// </para>
/// <para>
/// What comes back is a count. No address, contact, or account crosses this boundary in either direction beyond the
/// lookup itself, so a refusal built from the answer can say that somebody could not be vouched for without saying who
/// — and a caller cannot map the contact book by watching which sends are refused.
/// </para>
/// <para>
/// The book is read in groups rather than one address at a time, under the same bound the directory answers a whole
/// contact's addresses in, so a message costs a read per group instead of a read per recipient.
/// </para>
/// </remarks>
/// <param name="contacts">Reads which of a set of addresses the book already holds.</param>
/// <param name="accounts">Says which accounts this deployment serves.</param>
/// <param name="senderIdentities">Says which address each of those accounts sends as.</param>
public sealed class RecipientVouching(
    IContactDirectory contacts,
    IDeploymentMailAccountCatalog accounts,
    IOutgoingSenderIdentityReader senderIdentities)
{
    /// <summary>Counts the recipients a caller named itself that nothing this deployment holds vouches for.</summary>
    /// <param name="recipients">Everybody the message is addressed to, whoever put them there.</param>
    /// <param name="cancellationToken">Cancels the reads of the book.</param>
    /// <returns>How many of the addresses the caller supplied are ones this deployment has no trace of.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Text that names no mailbox at all is not counted here. Whether an address parses is the composition's question
    /// and it refuses one that does not, so counting unparsable text would refuse a send for a reason the caller is
    /// about to be told properly.
    /// </remarks>
    public async Task<int> CountUnvouchedAsync(
        IReadOnlyList<AuthoredEmailRecipient> recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        var namedByCaller = ParsedAddressesOf(recipients).Distinct().ToArray();

        if (namedByCaller.Length == 0)
        {
            return 0;
        }

        var ownAddresses = this.OwnAddresses();
        var unvouched = namedByCaller.Where(address => !ownAddresses.Contains(address)).ToArray();

        if (unvouched.Length == 0)
        {
            return 0;
        }

        var count = 0;

        foreach (var group in unvouched.Chunk(Contact.MaximumAddressCount))
        {
            var held = await contacts.FindHoldersOfAsync(group, cancellationToken);

            count += group.Count(address => !held.ContainsKey(address));
        }

        return count;
    }

    /// <summary>Reads the addresses a caller wrote down, dropping the text that names no mailbox at all.</summary>
    /// <remarks>
    /// Written as an iterator rather than as a projection because the parse carries its result out through a parameter,
    /// which is one of the shapes a query cannot express. The address it produces is the same normalized value the book
    /// is indexed by, so an address recorded in one spelling answers for a caller that wrote another.
    /// </remarks>
    private static IEnumerable<EmailAddress> ParsedAddressesOf(IReadOnlyList<AuthoredEmailRecipient> recipients)
    {
        foreach (var recipient in recipients)
        {
            if (recipient.Provenance is AuthoredRecipientProvenance.NamedByCaller
                && EmailAddress.TryCreate(displayName: null, recipient.Address, out var address))
            {
                yield return address;
            }
        }
    }

    /// <summary>Reads the mailboxes this deployment sends as, which are the owner's own and never a stranger's.</summary>
    /// <remarks>
    /// Read from configuration rather than from the database, so an installation that has synchronized nothing still
    /// vouches for its own addresses. An account this deployment serves without a sending identity contributes none,
    /// which is the honest answer: nothing here knows what a read-only account's own address is.
    /// </remarks>
    private HashSet<EmailAddress> OwnAddresses() => accounts.ServedAccounts
        .Select(account => senderIdentities.FindSenderIdentity(account.Id))
        .OfType<OutgoingSenderIdentity>()
        .Select(identity => identity.Address)
        .ToHashSet();
}
