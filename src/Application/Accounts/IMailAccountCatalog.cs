// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Names the mail accounts this deployment serves.</summary>
/// <remarks>
/// <para>
/// A query use case asks this before it reads anything, and it asks for two reasons. An account identifier nobody
/// configured is refused rather than answered with an empty page, because at a boundary a client reaches an empty page
/// tells the client the identifier exists and holds no mail, which turns a list operation into a way to enumerate
/// accounts. A request that names no account is then narrowed to this set rather than left unrestricted, because
/// removing an account from configuration must stop its stored mail from being readable even though its rows remain.
/// </para>
/// <para>
/// The set stays inside the application: a use case reads it to bound its own query, and nothing publishes it to a
/// caller. What a caller learns is only whether the identifier they themselves named was accepted.
/// </para>
/// </remarks>
public interface IMailAccountCatalog
{
    /// <summary>Gets the accounts this deployment is configured to serve, deduplicated and ordered.</summary>
    /// <remarks>
    /// The order is the ordinal order of the identifiers, so a scope resolved from this set is canonical and a
    /// continuation cursor issued for it stays valid while the configuration does not change. An empty set means no
    /// account is served and therefore that no stored mail is readable, which is a state configuration allows: an
    /// operator may switch synchronization off and remove every account while a local copy still exists.
    /// </remarks>
    IReadOnlyList<MailAccountId> ServedAccountIds { get; }
}
