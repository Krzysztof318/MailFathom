// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Accounts;

/// <summary>Describes the mail accounts the owner this unit of work is acting for owns, and no others.</summary>
/// <remarks>
/// <para>
/// Every caller-facing use case that resolves an account asks this rather than the deployment's own catalog. A query
/// use case asks it before it reads anything, and it asks for two reasons. An account this owner does not own is refused
/// exactly as one nobody configured, because a refusal that separated the two would tell a caller which accounts exist
/// beside their own. And a request that names no account is narrowed to this set rather than left unrestricted, because
/// an unbounded read would publish every owner's mail rather than merely the mail of an account an operator has since
/// removed.
/// </para>
/// <para>
/// An owner who owns nothing answers with an empty set, and that is a real answer rather than an absent one: the
/// resolution turns it into a scope that reads nothing, never into an unrestricted query. A principal acting for no
/// owner at all is a different case and is refused rather than answered — the deployment administrator and this
/// process's own identity reach this port only by mistake, and an empty answer would let that mistake look like an
/// owner with no mail.
/// </para>
/// <para>
/// The set is published to a caller in one place and one only: the tool that exists to say which accounts they may name.
/// Every other use of it is a bound on a query rather than an answer, and what a caller learns there is still only
/// whether the account it named was accepted.
/// </para>
/// </remarks>
public interface ICallerMailAccountCatalog
{
    /// <summary>Gets whether this deployment refreshes the local copy of these accounts at all.</summary>
    /// <remarks>
    /// The switch is the deployment's rather than the owner's, and it is answered here because a caller reading their
    /// own accounts has to be able to tell a mailbox that is merely quiet from one nothing is updating. It says nothing
    /// about which accounts they are.
    /// </remarks>
    bool SynchronizationEnabled { get; }

    /// <summary>Gets the owner this unit of work is acting for, which is whose mail every read narrowed here returns.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no owner.</exception>
    /// <remarks>
    /// Published beside the accounts because a query carries both: the owner is the first term of every mail-returning
    /// predicate and the column every index those reads are planned against leads with, while the accounts narrow
    /// within it. Reading it here rather than from the principal directly is what keeps the two answers one answer — a
    /// read narrowed on an owner the catalog did not answer for would be narrowed on an owner whose accounts it never
    /// listed.
    /// </remarks>
    MailOwnerId Owner { get; }

    /// <summary>Gets the accounts the owner in hand owns, deduplicated and ordered, or empty when they own none.</summary>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no owner.</exception>
    /// <remarks>
    /// Ordered the way <see cref="IDeploymentMailAccountCatalog.ServedAccounts" /> is, and for the same reason: a scope
    /// resolved from it is canonical, so a continuation cursor issued for it stays valid while neither the configuration
    /// nor what this owner owns changes.
    /// </remarks>
    IReadOnlyList<ServedMailAccount> OwnedAccounts { get; }
}
