// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.TestSupport;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Describes the accounts a test's deployment serves.</summary>
internal sealed class StubMailAccountCatalog(params string[] servedAccountIds) : IMailAccountCatalog
{
    /// <inheritdoc />
    public bool SynchronizationEnabled { get; init; } = true;

    /// <inheritdoc />
    public IReadOnlyList<ServedMailAccount> ServedAccounts { get; init; } =
        [.. servedAccountIds.Select(SyntheticServedAccount.Of)];
}
