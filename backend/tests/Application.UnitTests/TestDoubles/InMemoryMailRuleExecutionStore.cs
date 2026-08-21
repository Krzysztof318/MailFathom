// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The recorded rule history, kept in order so a test can assert what a pass explained about its own work.</summary>
/// <remarks>
/// The append is staged rather than applied, exactly as the real store's is: a batch's executions become visible only
/// when the session commits, which is what lets a test prove that a rolled-back attempt leaves no explanation behind.
/// </remarks>
internal sealed class InMemoryMailRuleExecutionStore : IMailRuleExecutionStore
{
    private readonly List<MailRuleExecution> committed = [];

    /// <summary>Gets every execution the history holds, in the order the passes appended them.</summary>
    internal IReadOnlyList<MailRuleExecution> Executions => this.committed;

    /// <summary>Gets what one rule concluded about the mail it was run over.</summary>
    /// <param name="ruleName">The rule to read.</param>
    /// <returns>Its executions, in the order they were appended.</returns>
    internal IReadOnlyList<MailRuleExecution> ExecutionsOf(string ruleName) =>
        [.. this.committed.Where(execution => StringComparer.Ordinal.Equals(execution.RuleName, ruleName))];

    /// <inheritdoc />
    public Task AppendAsync(
        IPersistenceSession session,
        IReadOnlyList<MailRuleExecution> executions,
        CancellationToken cancellationToken)
    {
        this.committed.AddRange(executions);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MailRuleExecutionPage> ReadPageAsync(
        MailRuleExecutionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matching = this.committed
            .Where(execution => execution.AccountId == query.AccountId)
            .Where(execution => query.RuleName is not { } ruleName
                || StringComparer.Ordinal.Equals(execution.RuleName, ruleName))
            .Where(execution => query.StoredEmailId is not { } emailId || execution.StoredEmailId == emailId)
            .OrderByDescending(execution => execution.EvaluatedAt)
            .Take(query.PageSize);

        return Task.FromResult(new MailRuleExecutionPage([.. matching], NextCursor: null));
    }

    /// <inheritdoc />
    public Task<int> EraseEvaluatedBeforeAsync(
        MailAccountId accountId,
        DateTimeOffset evaluatedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var expiring = this.committed
            .Where(execution => execution.AccountId == accountId && execution.EvaluatedAt < evaluatedBefore)
            .OrderBy(execution => execution.EvaluatedAt)
            .Take(limit)
            .ToArray();

        foreach (var execution in expiring)
        {
            this.committed.Remove(execution);
        }

        return Task.FromResult(expiring.Length);
    }
}
