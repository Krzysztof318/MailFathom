// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.Mcp.Tools;

/// <summary>Reads the name each account is published under, for the results that name one.</summary>
/// <remarks>
/// <para>
/// This is the outward half of what <see cref="MailboxScopeArguments" /> does inward: a use case answers in the domain
/// identities a query is expressed in, and a caller reads the names a deployment publishes. Doing it once here rather
/// than in each tool is what stops two tools from publishing an account under two different names.
/// </para>
/// <para>
/// The lookup is built per tool call, from the same catalog the use case bounded the read with — the caller-scoped one,
/// so the names a result carries are the names of the caller's own accounts and a reload is observed rather than cached
/// past. An ordinary dictionary rather than a frozen one for that reason: it is built once and read a few times per
/// call, which is the shape a frozen collection's construction cost is wrong for.
/// </para>
/// </remarks>
internal sealed class PublishedAccountNames
{
    private readonly Dictionary<string, string> displayNamesByAccountId;

    private PublishedAccountNames(Dictionary<string, string> displayNamesByAccountId) =>
        this.displayNamesByAccountId = displayNamesByAccountId;

    /// <summary>Reads the published names of the accounts the caller's owner owns.</summary>
    /// <param name="accountCatalog">Describes the accounts the caller's owner owns.</param>
    /// <returns>The lookup a result mapping reads names from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountCatalog" /> is <see langword="null" />.</exception>
    public static PublishedAccountNames From(ICallerMailAccountCatalog accountCatalog)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);

        return new PublishedAccountNames(
            accountCatalog.OwnedAccounts.ToDictionary(
                static account => account.Id.Value,
                static account => account.DisplayName.Value,
                StringComparer.Ordinal));
    }

    /// <summary>Reads the name one account is published under.</summary>
    /// <param name="accountId">The account a result names.</param>
    /// <returns>The account's display name, or its identifier when the caller's owner no longer owns it.</returns>
    /// <remarks>
    /// Every account a result can name is one the read was bounded to, so the fallback is reachable only when
    /// configuration was reloaded, or what the owner owns changed, between the query and the mapping of its answer. The
    /// identifier is what is published then, because MailFathom's own name for the account is a truthful answer and
    /// failing a read that already succeeded is not.
    /// </remarks>
    public string For(MailAccountId accountId) =>
        this.displayNamesByAccountId.TryGetValue(accountId.Value, out var displayName) ? displayName : accountId.Value;
}
