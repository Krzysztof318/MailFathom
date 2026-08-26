// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The rule history a supervised run writes to, holding what it was appended and erasing nothing.</summary>
/// <remarks>
/// The supervisor's run resolves this from its own scope twice over: once through the evaluation pass, which appends
/// what its rules concluded, and once through the retention pass, which erases what has outlived the window. These
/// tests configure no mail to evaluate, so both are exercised as the run's shape rather than for what they hold.
/// </remarks>
internal sealed class MailRuleExecutionRecordingStore : IMailRuleExecutionStore
{
    private readonly List<MailRuleExecution> appended = [];

    /// <summary>Gets every execution a run appended, in the order it appended them.</summary>
    internal IReadOnlyList<MailRuleExecution> Appended => this.appended;

    /// <inheritdoc />
    public Task AppendAsync(
        IPersistenceSession session,
        IReadOnlyList<MailRuleExecution> executions,
        CancellationToken cancellationToken)
    {
        this.appended.AddRange(executions);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MailRuleExecutionPage> ReadPageAsync(
        MailRuleExecutionQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MailRuleExecutionPage([], NextCursor: null));

    /// <inheritdoc />
    public Task<int> EraseEvaluatedBeforeAsync(
        MailAccountIdentity account,
        DateTimeOffset evaluatedBefore,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
