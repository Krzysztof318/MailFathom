// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes one account the caller's user owns.</summary>
/// <remarks>
/// <para>
/// It carries the two names a caller may use for the account and how current its local copy is, and nothing about how
/// MailFathom reaches the mailbox. The mail server, the port, the user name, and every credential are deliberately
/// absent: they are the operator's connection detail rather than a property of the mailbox, and a caller choosing which
/// mailbox to ask about needs none of them.
/// </para>
/// <para>
/// The identifier is the one the deployment generated for the account and is unique across the deployment; the display
/// name is unique only within the account's user. Both descriptions say so because a client stores what it reads here.
/// The user itself is not published: a caller learns which mailboxes are theirs to name, never that another user spells
/// one the same way.
/// </para>
/// </remarks>
[Description("One mail account you may read, with the names a request may use for it and how current the local copy of each of its folders is.")]
internal sealed record ListedMailAccount
{
    /// <summary>Gets the stable identifier the deployment generated for the account.</summary>
    [Description("The MailFathom account identifier the deployment generated. It is what every other result reports as accountId, and it is stable across a change of the display name. It is unique across the deployment, but it is only a name for this mailbox as your own account: an identifier you have not read from list_accounts names nothing you may use.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the name the account is published under, within its user.</summary>
    [Description("The display name given to the account, which is the readable name for the mailbox. Either this or accountId may be used to name the account when narrowing a listing, a search, or a question; the display name is matched without regard to case. It is unique among your own accounts rather than across the deployment, and it never collides with another of your accounts' identifiers, so either spelling names one mailbox.")]
    public required string DisplayName { get; init; }

    /// <summary>Gets what the operator asked to start the account's next synchronization pass.</summary>
    [Description("What the operator configured to start this account's next synchronization pass: 'polling' to reconcile on a fixed interval, or 'push' to hold a session that reacts to a change at once. It states what was asked for rather than what a folder is currently getting, which is decided per folder against what the mail server offers.")]
    public required AccountSynchronizationMode SynchronizationMode { get; init; }

    /// <summary>Gets how current the local copy of each of the account's folders is.</summary>
    [Description("How current the local copy of each of this account's folders is, one entry per folder local state knows of. Empty when synchronization has never reached the account, which means its mail may be absent entirely rather than merely out of date.")]
    public required IReadOnlyList<FolderCopyFreshness> Folders { get; init; }

    /// <summary>Publishes one account the use case described.</summary>
    /// <param name="account">The described account to publish.</param>
    /// <param name="accountNames">Reads the name each folder entry's account is published under.</param>
    /// <returns>The wire representation of <paramref name="account" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> or <paramref name="accountNames" /> is <see langword="null" />.</exception>
    public static ListedMailAccount From(DescribedMailAccount account, PublishedAccountNames accountNames)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(accountNames);

        return new ListedMailAccount
        {
            AccountId = account.Account.Id.Value,
            DisplayName = account.Account.DisplayName.Value,
            SynchronizationMode = Published(account.Account.SynchronizationMode),
            Folders = [.. account.Folders.Select(freshness => FolderCopyFreshness.From(freshness, accountNames))],
        };
    }

    /// <summary>Maps the configured mode onto the value this contract publishes.</summary>
    /// <remarks>
    /// A closed mapping rather than a cast, because the two enumerations are separate on purpose: the wire values are
    /// this boundary's to decide, and a mode the domain grew without a published name has to fail here rather than reach
    /// a client as a number nobody documented.
    /// </remarks>
    private static AccountSynchronizationMode Published(MailSynchronizationMode synchronizationMode) => synchronizationMode switch
    {
        MailSynchronizationMode.Polling => AccountSynchronizationMode.Polling,
        MailSynchronizationMode.Push => AccountSynchronizationMode.Push,
        _ => throw new ArgumentOutOfRangeException(
            nameof(synchronizationMode),
            synchronizationMode,
            "The synchronization mode has no published wire value."),
    };
}
