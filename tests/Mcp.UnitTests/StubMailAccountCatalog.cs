// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Names the accounts a test's deployment serves.</summary>
internal sealed class StubMailAccountCatalog(params string[] servedAccountIds) : IMailAccountCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<MailAccountId> ServedAccountIds { get; } =
        [.. servedAccountIds.Select(MailAccountId.Create)];
}
