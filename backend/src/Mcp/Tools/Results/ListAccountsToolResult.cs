// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Accounts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes the accounts the caller's owner owns.</summary>
/// <remarks>
/// The record is the tool's structured output, so its shape is the advertised output schema and its descriptions travel
/// with it. There is no cursor: the set is one owner's accounts rather than a mailbox, and an owner holding so many
/// accounts that they had to be paged would be a different problem from the one this answers.
/// </remarks>
[Description("The mail accounts you may read, with the names a request may use for each and how current their local copies are.")]
internal sealed record ListAccountsToolResult
{
    /// <summary>Gets the accounts the caller's owner owns, ordered by identifier.</summary>
    [Description("The accounts you may read, ordered by account identifier. Empty when you may read none, which means no stored mail is readable at all rather than that the mailboxes are empty.")]
    public required IReadOnlyList<ListedMailAccount> Accounts { get; init; }

    /// <summary>Gets whether the deployment refreshes the local copy of these accounts at all.</summary>
    [Description("Whether this deployment is refreshing its local copy of these mailboxes. False means synchronization is switched off: every read still answers from what was already stored, and nothing new will arrive, so the per-folder timestamps are as current as the answers will get.")]
    public required bool SynchronizationEnabled { get; init; }

    /// <summary>Publishes the directory the use case read.</summary>
    /// <param name="directory">The owner's accounts to publish.</param>
    /// <param name="accountNames">Reads the name each account is published under.</param>
    /// <returns>The wire representation of <paramref name="directory" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory" /> or <paramref name="accountNames" /> is <see langword="null" />.</exception>
    public static ListAccountsToolResult From(MailAccountDirectory directory, PublishedAccountNames accountNames)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(accountNames);

        return new ListAccountsToolResult
        {
            Accounts = [.. directory.Accounts.Select(account => ListedMailAccount.From(account, accountNames))],
            SynchronizationEnabled = directory.SynchronizationEnabled,
        };
    }
}
