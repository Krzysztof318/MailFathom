// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.Evaluations.Reporting;

/// <summary>A response cache that keeps, per scenario and iteration, a tally of the answers read from it and the ones asked afresh.</summary>
/// <param name="inner">The cache the entries live in.</param>
/// <remarks>
/// The tally is kept here because the cache provider is the one member of a run's <see cref="ReportingConfiguration" />
/// the suite supplies, so it is what every scenario of the run and the code filing its verdicts can reach alike.
/// </remarks>
internal sealed class TallyingResponseCacheProvider(IEvaluationResponseCacheProvider inner) : IEvaluationResponseCacheProvider
{
    private readonly ConcurrentDictionary<(string Scenario, string Iteration), CachedAnswerTally> tallies = new();

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

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default) => inner.ResetAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteExpiredCacheEntriesAsync(CancellationToken cancellationToken = default) =>
        inner.DeleteExpiredCacheEntriesAsync(cancellationToken);
}
