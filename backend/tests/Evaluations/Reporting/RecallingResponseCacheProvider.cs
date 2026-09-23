// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Caching.Distributed;

namespace MailFathom.Evaluations.Reporting;

/// <summary>A response cache that remembers which entries each scenario and iteration touched, so a shortfall can be forgotten.</summary>
/// <param name="inner">The cache the entries live in.</param>
/// <remarks>
/// A cached answer that fell short would otherwise be read back by every later run, and a single miss would
/// fail every run that followed it. An entry is remembered when it is read back as well as when it is written, because an answer
/// that fell short on this run may have been paid for on an earlier one.
/// </remarks>
internal sealed class RecallingResponseCacheProvider(IEvaluationResponseCacheProvider inner) : IEvaluationResponseCacheProvider
{
    private readonly ConcurrentDictionary<(string Scenario, string Iteration), RecallingCache> caches = new();

    /// <inheritdoc />
    public async ValueTask<IDistributedCache> GetCacheAsync(
        string scenarioName,
        string iterationName,
        CancellationToken cancellationToken = default)
    {
        var cache = await inner.GetCacheAsync(scenarioName, iterationName, cancellationToken);

        return this.caches.GetOrAdd((scenarioName, iterationName), _ => new RecallingCache(cache));
    }

    /// <summary>Removes every entry the scenario and iteration touched through this provider.</summary>
    /// <param name="scenarioName">The scenario the entries are filed under.</param>
    /// <param name="iterationName">The iteration the entries are filed under.</param>
    /// <param name="cancellationToken">Withdraws the removal.</param>
    /// <returns>A task that completes once every entry is gone.</returns>
    public async Task ForgetAsync(string scenarioName, string iterationName, CancellationToken cancellationToken)
    {
        if (this.caches.TryRemove((scenarioName, iterationName), out var cache))
        {
            await cache.ForgetAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default) => inner.ResetAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteExpiredCacheEntriesAsync(CancellationToken cancellationToken = default) =>
        inner.DeleteExpiredCacheEntriesAsync(cancellationToken);

    private sealed class RecallingCache(IDistributedCache inner) : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte> touched = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => this.Touch(key, inner.Get(key));

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            this.Touch(key, await inner.GetAsync(key, token));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            inner.Set(key, value, options);
            this.Touch(key, value);
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            await inner.SetAsync(key, value, options, token);
            this.Touch(key, value);
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);

        public async Task ForgetAsync(CancellationToken cancellationToken)
        {
            foreach (var key in this.touched.Keys)
            {
                await inner.RemoveAsync(key, cancellationToken);
            }
        }

        /// <summary>Remembers a key that holds an entry, since removing one that holds none throws.</summary>
        private byte[]? Touch(string key, byte[]? entry)
        {
            if (entry is not null)
            {
                this.touched.TryAdd(key, 0);
            }

            return entry;
        }
    }
}
