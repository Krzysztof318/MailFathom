// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The one whole-mailbox classification run an account may have, keyed by the account exactly as the table is.</summary>
internal sealed class InMemorySpamClassificationRunStore : ISpamClassificationRunStore
{
    private readonly Dictionary<MailAccountIdentity, SpamClassificationRun> runs = [];
    private readonly List<SpamClassificationRun> saves = [];

    /// <summary>Gets every state a run was saved in, which is what proves a batch committed the position it reached.</summary>
    internal IReadOnlyList<SpamClassificationRun> Saves => this.saves;

    /// <summary>Gets the run recorded for an account, whether or not it is still outstanding.</summary>
    /// <param name="account">The account to read, named as the owner and the identifier together.</param>
    /// <returns>The run, or <see langword="null" /> when the account has never had one.</returns>
    internal SpamClassificationRun? Find(MailAccountIdentity account) => this.runs.GetValueOrDefault(account);

    /// <summary>Puts a run in front of an account without going through the request path.</summary>
    /// <param name="run">The run to record.</param>
    internal void Arrange(SpamClassificationRun run) => this.runs[run.Account] = run;

    /// <inheritdoc />
    public Task<SpamClassificationRun?> FindOutstandingAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(account) is { IsOutstanding: true } outstanding
            ? outstanding
            : null);

    /// <inheritdoc />
    public Task<SpamClassificationRun?> FindLatestAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(account));

    /// <inheritdoc />
    public Task SaveAsync(IPersistenceSession session, SpamClassificationRun run, CancellationToken cancellationToken)
    {
        this.runs[run.Account] = run;
        this.saves.Add(run);

        return Task.CompletedTask;
    }
}
