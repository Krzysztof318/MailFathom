// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>Describes the mail accounts this deployment serves.</summary>
/// <remarks>
/// <para>
/// A query use case asks this before it reads anything, and it asks for two reasons. An account nobody configured is
/// refused rather than answered with an empty page, because at a boundary a client reaches an empty page tells the
/// client the name exists and holds no mail, which turns a list operation into a way to enumerate accounts. A request
/// that names no account is then narrowed to this set rather than left unrestricted, because removing an account from
/// configuration must stop its stored mail from being readable even though its rows remain.
/// </para>
/// <para>
/// The set is published to a caller in one place and one only: the tool that exists to say which accounts a deployment
/// serves, so that a client can name one. Every other use of it is a bound on a query rather than an answer, and what a
/// caller learns there is still only whether the account it named was accepted.
/// </para>
/// </remarks>
public interface IMailAccountCatalog
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
