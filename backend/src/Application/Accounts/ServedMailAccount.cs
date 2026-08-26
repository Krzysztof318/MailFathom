// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Application.Accounts;

/// <summary>Describes one mail account this deployment serves, as configuration declares it.</summary>
/// <param name="Owner">The owner the account belongs to, which is the other half of what identifies it.</param>
/// <param name="Id">The identifier every stored row, cursor, and log line names the account by, within its owner.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person reading a result recognizes.</param>
/// <param name="SynchronizationMode">What the operator asked to start the account's next synchronization pass.</param>
/// <remarks>
/// <para>
/// It carries what an operator decided about the account and nothing about how MailFathom reaches it. The host, the
/// port, the user name, and every secret reference are deliberately absent: they are the deployment's connection detail
/// rather than a property of the mailbox, and this record travels to places — a published tool result among them — where
/// none of them may go. The owner is a property of the mailbox rather than a connection detail, which is why it is here
/// and why nothing publishes it: a caller learns which accounts they may name, never whose the others are.
/// </para>
/// <para>
/// The owner is here so that a write which has resolved an account has already resolved whose it is. Every row that
/// references an account carries the owner beside it, and a store filling that column from this record is filling it
/// from the resolution it already performed rather than from a second read of the account table. While accounts are
/// declared in configuration the value is the deployment's one owner; when they become rows it comes off the account
/// row, and this is the one place that changes.
/// </para>
/// <para>
/// The mode states the operator's request rather than what a folder actually got. Whether push is served is decided per
/// folder against what the mail server advertises and how recent attempts went, which is a synchronization observation
/// and is reported where those are.
/// </para>
/// </remarks>
public sealed record ServedMailAccount(
    MailOwnerId Owner,
    MailAccountId Id,
    MailAccountDisplayName DisplayName,
    MailSynchronizationMode SynchronizationMode)
{
    /// <summary>Gets the account's full identity, which is what a write records an account reference by.</summary>
    public MailAccountIdentity Identity => MailAccountIdentity.Create(this.Owner, this.Id);

    /// <summary>Reports whether text a request carried names this account.</summary>
    /// <param name="selector">The text naming an account.</param>
    /// <returns><see langword="true" /> when the text is this account's identifier or its display name.</returns>
    /// <remarks>
    /// The identifier is matched ordinally because it is a configured key that everything else compares exactly, and
    /// the display name without regard to case because it is prose an operator wrote for a person to retype. Both are
    /// compared against the whole value: a display name is never matched as a fragment, so naming one account can never
    /// select another whose name contains it.
    /// <para>
    /// It answers about one account and says nothing about which set the account was drawn from, which is what makes
    /// asking it of another owner's accounts a mistake rather than a refusal. Both names are unique within the owner
    /// that gave them and nowhere wider, so two owners may each answer to <c>work</c>: a caller-facing resolution asks
    /// this only of the accounts the caller's owner owns, and one that asked it of the deployment's would resolve the
    /// caller's word to whichever owner's account came first.
    /// </para>
    /// </remarks>
    public bool IsNamedBy(MailAccountSelector selector) =>
        StringComparer.Ordinal.Equals(this.Id.Value, selector.Value)
        || StringComparer.OrdinalIgnoreCase.Equals(this.DisplayName.Value, selector.Value);
}
