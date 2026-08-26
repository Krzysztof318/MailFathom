// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes one account the caller's owner owns.</summary>
/// <remarks>
/// <para>
/// It carries the two names a caller may use for the account and how current its local copy is, and nothing about how
/// MailFathom reaches the mailbox. The mail server, the port, the user name, and every credential are deliberately
/// absent: they are the operator's connection detail rather than a property of the mailbox, and a caller choosing which
/// mailbox to ask about needs none of them.
/// </para>
/// <para>
/// Both names belong to the account's owner and are unique within it rather than across the deployment, and both
/// descriptions say so because a client stores what it reads here. The owner itself is not published: a caller learns
/// which mailboxes are theirs to name, never that another owner spells one the same way.
/// </para>
/// </remarks>
[Description("One mail account you may read, with the names a request may use for it and how current the local copy of each of its folders is.")]
internal sealed record ListedMailAccount
{
    /// <summary>Gets the stable identifier the account is configured under, within its owner.</summary>
    [Description("The configured MailFathom account identifier. It is what every other result reports as accountId, and it is stable across a change of the display name. It is unique within the account's owner rather than across the deployment, so store it as this owner's name for the mailbox and never compare it with an identifier that reached you from another owner or another deployment.")]
    public required string AccountId { get; init; }

    /// <summary>Gets the name the account is published under, within its owner.</summary>
    [Description("The display name the operator gave the account, which is the readable name for the mailbox. Either this or accountId may be used to name the account when narrowing a listing, a search, or a question; the display name is matched without regard to case. It is unique within the account's owner in the same way accountId is, and the two names share one naming space there, so either spelling names one mailbox.")]
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
