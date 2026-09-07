// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Describes the accounts a test's deployment serves, and the accounts its one user owns.</summary>
/// <remarks>
/// It answers both catalogs with one set, because a tool test arranges a deployment serving one user and the two
/// answers are the same there. A test about the difference between them arranges the two separately rather than
/// reaching for this.
/// </remarks>
internal sealed class StubMailAccountCatalog(params string[] servedAccountIds)
    : IDeploymentMailAccountCatalog, ICallerMailAccountCatalog
{
    /// <inheritdoc />
    public bool SynchronizationEnabled { get; init; } = true;

    /// <inheritdoc />
    public IReadOnlyList<ServedMailAccount> ServedAccounts { get; init; } =
        [.. servedAccountIds.Select(accountId => SyntheticServedAccount.Of(accountId))];

    /// <inheritdoc />
    public IReadOnlyList<ServedMailAccount> OwnedAccounts => this.ServedAccounts;

    /// <inheritdoc />
    /// <remarks>
    /// The user every account here belongs to, which is the deployment's one user unless a test served an account of
    /// somebody else's. Read from the accounts rather than stated again, so the two halves cannot disagree.
    /// </remarks>
    public MailUserId User =>
        this.ServedAccounts.Count is 0 ? SyntheticMailUser.Deployment : this.ServedAccounts[0].User;
}
