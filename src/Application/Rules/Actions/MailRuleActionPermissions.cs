// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Rules.Actions;

/// <summary>States which of the four mutations a rule may ask for on one account.</summary>
/// <param name="PermitsRelocate">Whether a rule may move mail into another folder.</param>
/// <param name="PermitsCopy">Whether a rule may put a second occurrence into another folder.</param>
/// <param name="PermitsDelete">Whether a rule may remove mail from the folder it is in.</param>
/// <param name="PermitsSetSeen">Whether a rule may set or clear the remote <c>\Seen</c> flag.</param>
/// <remarks>
/// <para>
/// What automation may do to a mailbox is the owner's decision rather than a rule's, so it is declared per account and
/// per mutation instead of as one switch: an installation can run every rule it has with deletion refused, which is the
/// case this exists for. A rule declaring an action its account does not permit is refused when the configuration is
/// read rather than skipped later, because a rule that silently does nothing is indistinguishable from one that never
/// matched.
/// </para>
/// <para>
/// Deletion is the one action that is opt-in. The other three are reversible from an ordinary mail client — a message
/// filed can be moved back, a copy removed, a flag cleared — and a deletion is the one that is not, so an account
/// saying nothing about it means no.
/// </para>
/// </remarks>
public sealed record MailRuleActionPermissions(
    bool PermitsRelocate,
    bool PermitsCopy,
    bool PermitsDelete,
    bool PermitsSetSeen)
{
    /// <summary>Gets the permissions of an account that says nothing: every reversible change, and no deletion.</summary>
    public static MailRuleActionPermissions Default { get; } = new(
        PermitsRelocate: true,
        PermitsCopy: true,
        PermitsDelete: false,
        PermitsSetSeen: true);

    /// <summary>Reports whether a rule on this account may ask for one mutation.</summary>
    /// <param name="mutation">The mutation the rule declares.</param>
    /// <returns><see langword="true" /> when the account permits it.</returns>
    /// <remarks>
    /// A closed enumeration's members are not compile-time constants, so the answer is reached by comparison rather
    /// than by a switch over cases. An unspecified mutation is permitted by nothing, which is what the final answer
    /// gives it.
    /// </remarks>
    public bool Permits(MailboxMutation mutation)
    {
        if (mutation == MailboxMutation.Relocate)
        {
            return this.PermitsRelocate;
        }

        if (mutation == MailboxMutation.Copy)
        {
            return this.PermitsCopy;
        }

        if (mutation == MailboxMutation.Delete)
        {
            return this.PermitsDelete;
        }

        return mutation == MailboxMutation.SetSeen && this.PermitsSetSeen;
    }
}
