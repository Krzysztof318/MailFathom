// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.Evaluations.Reporting;

/// <summary>A response cache that keeps, per scenario and iteration, a tally of where each answer came from, and which entries this run wrote.</summary>
/// <param name="inner">The cache the entries live in.</param>
/// <param name="repetitions">How many times the run asks each case of each model.</param>
/// <remarks>
/// The tally, the entries written, and the count are kept here because the cache provider is the one member of a run's
/// <see cref="ReportingConfiguration" /> the suite supplies, so it is what every scenario of the run and the code filing
/// its verdicts can reach alike.
/// </remarks>
internal sealed class TallyingResponseCacheProvider(IEvaluationResponseCacheProvider inner, int repetitions)
    : IEvaluationResponseCacheProvider
{
    private readonly ConcurrentDictionary<(string Scenario, string Iteration), CachedAnswerTally> tallies = new();

    private readonly ConcurrentDictionary<(string Scenario, string Iteration), ConcurrentDictionary<string, byte>> writtenKeys = new();

    /// <summary>Gets how many times the run asks each case of each model, which joins the key every answer is cached under.</summary>
    public int Repetitions => repetitions;

    /// <inheritdoc />
    public ValueTask<IDistributedCache> GetCacheAsync(
        string scenarioName,
        string iterationName,
        CancellationToken cancellationToken = default) =>
        inner.GetCacheAsync(scenarioName, iterationName, cancellationToken);

    /// <summary>Gets the tally of the answers the model under test gave under one scenario and iteration.</summary>
    /// <param name="scenarioName">The scenario the answers are filed under.</param>
    /// <param name="iterationName">The iteration the answers are filed under.</param>
    /// <returns>The tally, empty until an answer is asked for.</returns>
    public CachedAnswerTally TallyFor(string scenarioName, string iterationName) =>
        this.tallies.GetOrAdd((scenarioName, iterationName), static _ => new CachedAnswerTally());

    /// <summary>Gets the keys this run has written under one scenario and iteration's cache.</summary>
    /// <param name="scenarioName">The scenario the cache is opened for.</param>
    /// <param name="iterationName">The iteration the cache is opened for, which is the one <see cref="GetCacheAsync" /> was given.</param>
    /// <returns>The keys, as a set every client over the same cache shares; empty until this run writes an entry there.</returns>
    /// <remarks>
    /// A scenario opens its store once and asks every question of its run through it, and the provider lives exactly as
    /// long as that store, so an entry is this run's own when its key is in here: what the cache held before the store
    /// opened was written by an earlier run.
    /// </remarks>
    public ConcurrentDictionary<string, byte> WrittenKeysFor(string scenarioName, string iterationName) =>
        this.writtenKeys.GetOrAdd((scenarioName, iterationName), static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default) => inner.ResetAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteExpiredCacheEntriesAsync(CancellationToken cancellationToken = default) =>
        inner.DeleteExpiredCacheEntriesAsync(cancellationToken);
}
