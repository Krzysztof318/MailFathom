// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Describes the accounts a test's deployment serves, and the accounts its one caller is assigned.</summary>
/// <remarks>
/// It answers both catalogs with one set, because a tool test arranges a deployment whose caller is assigned every
/// mailbox it serves and the two answers are the same there. A test about who reaches what arranges the two
/// separately rather than reaching for this.
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
    public IReadOnlyList<ServedMailAccount> AssignedAccounts => this.ServedAccounts;

    /// <inheritdoc />
    /// <remarks>
    /// The caller this stub acts for, which is the deployment's own user unless a test states another. No account
    /// carries one any more — a mailbox is assigned rather than owned — so it is stated here rather than derived.
    /// </remarks>
    public UserId User { get; init; } = SyntheticUser.Deployment;
}
