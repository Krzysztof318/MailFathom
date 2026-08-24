// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
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
        [.. servedAccountIds.Select(SyntheticServedAccount.Of)];

    /// <inheritdoc />
    public IReadOnlyList<ServedMailAccount> OwnedAccounts => this.ServedAccounts;
}
