// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Describes the accounts a test's deployment serves, and the accounts its one owner owns.</summary>
/// <remarks>
/// It answers both catalogs with one set, because a tool test arranges a deployment serving one owner and the two
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
    /// The owner every account here belongs to, which is the deployment's one owner unless a test served an account of
    /// somebody else's. Read from the accounts rather than stated again, so the two halves cannot disagree.
    /// </remarks>
    public MailOwnerId Owner =>
        this.ServedAccounts.Count is 0 ? SyntheticMailOwner.Deployment : this.ServedAccounts[0].Owner;
}
