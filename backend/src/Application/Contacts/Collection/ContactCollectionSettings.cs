// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts.Collection;

namespace MailFathom.Application.Contacts.Collection;

/// <summary>What one account collects, and under what bounds.</summary>
/// <remarks>
/// <para>
/// Collection is off unless an owner switched it on, and it is switched on per account rather than per deployment. An
/// instance builds a record of who writes to its owner only because somebody asked it to, and the accounts an instance
/// synchronizes are different correspondence: a work mailbox's counterparties are not a personal one's.
/// </para>
/// <para>
/// The two numbers bound different things. <see cref="MinimumMessagesFromSender" /> decides who is a correspondent
/// rather than somebody who wrote once, and <see cref="MaxContactsPerRun" /> decides how fast a book may fill — an
/// initial synchronization of a mailbox of years reaches the same book over many runs rather than in one.
/// </para>
/// </remarks>
public sealed record ContactCollectionSettings
{
    /// <summary>What an account with collection switched off is served, which records nothing at all.</summary>
    public static readonly ContactCollectionSettings CollectingNothing = new()
    {
        IsEnabled = false,
        MinimumMessagesFromSender = 1,
        MaxContactsPerRun = 0,
        Policy = ContactCollectionPolicy.NothingExcluded,
    };

    /// <summary>Gets whether this account records anybody at all.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Gets how many messages an address must have written before the person behind it is recorded.</summary>
    /// <remarks>
    /// Counted over the mail this account already holds, including the message being synchronized, so a value of one
    /// records a correspondent on first sight and the default of two records them on the second message. It bounds only
    /// the addresses that wrote to the owner: an address the owner themselves wrote to is recorded at once, because the
    /// owner having addressed somebody is the evidence this count is otherwise looking for.
    /// </remarks>
    public required int MinimumMessagesFromSender { get; init; }

    /// <summary>Gets how many contacts one folder synchronization run may record.</summary>
    public required int MaxContactsPerRun { get; init; }

    /// <summary>Gets which addresses this account is willing to have recorded.</summary>
    public required ContactCollectionPolicy Policy { get; init; }
}
