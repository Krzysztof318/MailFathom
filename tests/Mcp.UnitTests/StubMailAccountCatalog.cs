// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Domain.Accounts;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Names the accounts a test's deployment serves.</summary>
internal sealed class StubMailAccountCatalog(params string[] servedAccountIds) : IMailAccountCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<MailAccountId> ServedAccountIds { get; } =
        [.. servedAccountIds.Select(MailAccountId.Create)];
}
