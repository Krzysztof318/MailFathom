// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Actions;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>States which changes a rule may make to one account's mailbox.</summary>
/// <remarks>
/// <para>
/// Four switches rather than one, because what an owner is willing to let automation do is not one decision: filing
/// mail and marking it read are undone from any mail client, and a deletion is not. An account that says nothing gets
/// the three reversible actions and no deletion, so deletion is opt-in on every account of every deployment.
/// </para>
/// <para>
/// It is enforced where the rule set is read. A rule declaring an action one of its accounts refuses fails startup
/// naming both, rather than being carried and quietly skipped when that account's mail reaches it — a rule that does
/// nothing is indistinguishable from a rule that never matched, and this is exactly the setting an owner would check
/// last.
/// </para>
/// <para>
/// It is read a second time as each change is written down, through
/// <see cref="IMailRuleActionPermissionReader" />, because this section reloads independently of the rule section: an
/// operator narrowing what an account permits leaves a rule set nobody edited in force, and the withdrawal has to
/// reach the next pass rather than the next edit of the rules.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRuleActionPermissionOptions
{
    /// <summary>Gets or sets whether a rule may move this account's mail into another folder.</summary>
    public bool Move { get; set; } = true;

    /// <summary>Gets or sets whether a rule may put a second occurrence of this account's mail into another folder.</summary>
    public bool Copy { get; set; } = true;

    /// <summary>Gets or sets whether a rule may remove this account's mail from the folder it is in.</summary>
    public bool Delete { get; set; }

    /// <summary>Gets or sets whether a rule may set or clear the remote <c>\Seen</c> flag of this account's mail.</summary>
    public bool MarkAsRead { get; set; } = true;

    /// <summary>Reads the block as the permissions a rule set is judged against.</summary>
    /// <returns>The permissions.</returns>
    internal MailRuleActionPermissions ToPermissions() =>
        new(this.Move, this.Copy, this.Delete, this.MarkAsRead);
}
