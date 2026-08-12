// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The one whole-mailbox rule run an account may have, keyed by the account exactly as the table is.</summary>
internal sealed class InMemoryMailRuleEvaluationRunStore : IMailRuleEvaluationRunStore
{
    private readonly Dictionary<string, MailRuleEvaluationRun> runs = new(StringComparer.Ordinal);
    private readonly List<MailRuleEvaluationRun> saves = [];

    /// <summary>Gets every state a run was saved in, which is what proves a batch committed its position.</summary>
    internal IReadOnlyList<MailRuleEvaluationRun> Saves => this.saves;

    /// <summary>Gets the run recorded for an account, whether or not it is still outstanding.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <returns>The run, or <see langword="null" /> when the account has never had one.</returns>
    internal MailRuleEvaluationRun? Find(MailAccountId accountId) =>
        this.runs.GetValueOrDefault(accountId.Value);

    /// <summary>Puts a run in front of an account without going through the request path.</summary>
    /// <param name="run">The run to record.</param>
    internal void Arrange(MailRuleEvaluationRun run) => this.runs[run.AccountId.Value] = run;

    /// <inheritdoc />
    public Task<MailRuleEvaluationRun?> FindOutstandingAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(accountId.Value) is { IsOutstanding: true } outstanding
            ? outstanding
            : null);

    /// <inheritdoc />
    public Task<MailRuleEvaluationRun?> FindLatestAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(accountId.Value));

    /// <inheritdoc />
    public Task SaveAsync(IPersistenceSession session, MailRuleEvaluationRun run, CancellationToken cancellationToken)
    {
        this.runs[run.AccountId.Value] = run;
        this.saves.Add(run);

        return Task.CompletedTask;
    }
}
