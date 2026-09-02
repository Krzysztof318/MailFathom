// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The one whole-mailbox rule run an account may have, keyed by the account exactly as the table is.</summary>
internal sealed class InMemoryMailRuleEvaluationRunStore : IMailRuleEvaluationRunStore
{
    private readonly Dictionary<MailAccountIdentity, MailRuleEvaluationRun> runs = [];
    private readonly List<MailRuleEvaluationRun> saves = [];

    /// <summary>Gets every state a run was saved in, which is what proves a batch committed its position.</summary>
    internal IReadOnlyList<MailRuleEvaluationRun> Saves => this.saves;

    /// <summary>Gets or sets what happens at the moment a start reaches the store, before it reads the account's row.</summary>
    /// <remarks>
    /// Stands in for a competing transaction committing in the window between a request deciding what it wants and the
    /// write that claims the account. A request that made its decision from an earlier read would be unaffected by this
    /// and would overwrite whatever it arranged, which is exactly what the guard exists to stop.
    /// </remarks>
    internal Action? WhenAStartIsAttempted { get; set; }

    /// <summary>Gets the run recorded for an account, whether or not it is still outstanding.</summary>
    /// <param name="account">The account to read, named as the owner and the identifier together.</param>
    /// <returns>The run, or <see langword="null" /> when the account has never had one.</returns>
    internal MailRuleEvaluationRun? Find(MailAccountIdentity account) => this.runs.GetValueOrDefault(account);

    /// <summary>Puts a run in front of an account without going through the request path.</summary>
    /// <param name="run">The run to record.</param>
    internal void Arrange(MailRuleEvaluationRun run) => this.runs[run.Account] = run;

    /// <inheritdoc />
    public Task<MailRuleEvaluationRun?> FindOutstandingAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(account) is { IsOutstanding: true } outstanding
            ? outstanding
            : null);

    /// <inheritdoc />
    public Task<MailRuleEvaluationRun?> FindLatestAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(account));

    /// <inheritdoc />
    public Task SaveAsync(IPersistenceSession session, MailRuleEvaluationRun run, CancellationToken cancellationToken)
    {
        this.runs[run.Account] = run;
        this.saves.Add(run);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decides from the dictionary at the moment of the write, which is what the real store's own read inside the
    /// session does. A test arranging a run between a request's read and its write therefore sees the same answer here
    /// as it would from PostgreSQL.
    /// </remarks>
    public Task<MailRuleEvaluationRun?> TryStartAsync(
        IPersistenceSession session,
        MailRuleEvaluationRun run,
        CancellationToken cancellationToken)
    {
        this.WhenAStartIsAttempted?.Invoke();

        if (this.runs.GetValueOrDefault(run.Account) is { } claimed && !run.Supersedes(claimed))
        {
            return Task.FromResult<MailRuleEvaluationRun?>(claimed);
        }

        this.runs[run.Account] = run;
        this.saves.Add(run);

        return Task.FromResult<MailRuleEvaluationRun?>(null);
    }
}
