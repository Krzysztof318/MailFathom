// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Application.Accounts;

/// <summary>Describes one mail account this deployment serves, as configuration declares it.</summary>
/// <param name="Id">The stable identifier every stored row, cursor, and log line names the account by.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person reading a result recognizes.</param>
/// <param name="SynchronizationMode">What the operator asked to start the account's next synchronization pass.</param>
/// <remarks>
/// <para>
/// It carries what an operator decided about the account and nothing about how MailFathom reaches it. The host, the
/// port, the user name, and every secret reference are deliberately absent: they are the deployment's connection detail
/// rather than a property of the mailbox, and this record travels to places — a published tool result among them — where
/// none of them may go.
/// </para>
/// <para>
/// The mode states the operator's request rather than what a folder actually got. Whether push is served is decided per
/// folder against what the mail server advertises and how recent attempts went, which is a synchronization observation
/// and is reported where those are.
/// </para>
/// </remarks>
public sealed record ServedMailAccount(
    MailAccountId Id,
    MailAccountDisplayName DisplayName,
    MailSynchronizationMode SynchronizationMode)
{
    /// <summary>Reports whether text a request carried names this account.</summary>
    /// <param name="selector">The text naming an account.</param>
    /// <returns><see langword="true" /> when the text is this account's identifier or its display name.</returns>
    /// <remarks>
    /// The identifier is matched ordinally because it is a configured key that everything else compares exactly, and
    /// the display name without regard to case because it is prose an operator wrote for a person to retype. Both are
    /// compared against the whole value: a display name is never matched as a fragment, so naming one account can never
    /// select another whose name contains it.
    /// </remarks>
    public bool IsNamedBy(MailAccountSelector selector) =>
        StringComparer.Ordinal.Equals(this.Id.Value, selector.Value)
        || StringComparer.OrdinalIgnoreCase.Equals(this.DisplayName.Value, selector.Value);
}
