// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>Describes every mail account this deployment serves, whoever owns it.</summary>
/// <remarks>
/// <para>
/// This is the deployment's own answer and it belongs to work the deployment does for itself: the synchronization
/// coordinator that starts a run per account, the schedule that evaluates rules against each of them, the status an
/// operator reads, and the administrative operations an operator performs on an account they name. None of those acts
/// for one owner, and each of them would be wrong if it saw one owner's half of the deployment.
/// </para>
/// <para>
/// It is deliberately not what a caller-facing use case reads. A read that answers a person about their own mail asks
/// <see cref="ICallerMailAccountCatalog" /> instead, and the two are separate ports with differently named members so a
/// read model that reaches for the wrong one names the wrong member rather than compiling and answering across owners.
/// The rule is on the operation rather than on the surface: an administrative operation reaches this one, and an
/// operation a person performs about their own accounts reaches the other, and an operation that is both is two
/// operations.
/// </para>
/// <para>
/// An account nobody configured is refused rather than answered with an empty page, because at a boundary a client
/// reaches, an empty page tells the client the name exists and holds no mail, which turns a list operation into a way to
/// enumerate accounts.
/// </para>
/// </remarks>
public interface IDeploymentMailAccountCatalog
{
    /// <summary>Gets whether this deployment refreshes the local copy of these accounts at all.</summary>
    /// <remarks>
    /// It reports the operator's synchronization switch, which is a fact about the deployment rather than about any one
    /// account. Nothing gates account membership on it: a deployment that switched synchronization off still serves the
    /// mail it already stored, and a reader that saw only the accounts would have no way to tell a mailbox that is
    /// merely quiet from one nothing is updating.
    /// </remarks>
    bool SynchronizationEnabled { get; }

    /// <summary>Gets the accounts this deployment is configured to serve, deduplicated and ordered.</summary>
    /// <remarks>
    /// The order is the ordinal order of the identifiers, so a scope resolved from this set is canonical and a
    /// continuation cursor issued for it stays valid while the configuration does not change. An empty set means no
    /// account is served and therefore that no stored mail is readable, which is a state configuration allows: an
    /// operator may switch synchronization off and remove every account while a local copy still exists.
    /// </remarks>
    IReadOnlyList<ServedMailAccount> ServedAccounts { get; }
}
