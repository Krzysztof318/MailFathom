// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Holds what each signing domain published for a selector, for as long as that answer stays good.</summary>
/// <remarks>
/// <para>
/// The cache is what keeps the lookups proportionate. A mailbox receives many messages from few domains, and a domain
/// signs everything it sends with the same handful of selectors, so a synchronization run over a folder resolves a
/// dozen names rather than one per message — and a re-derivation over a whole mailbox, which is where the cost would
/// otherwise be felt, resolves each name once instead of tens of thousands of times.
/// </para>
/// <para>
/// It is bounded in entries as well as in time, because the key is derived from mail that arrives rather than from
/// anything this deployment configures: a sender writing from thousands of subdomains would otherwise grow the cache
/// without limit. Reaching the bound empties the cache instead of evicting by an order nothing here has a use for —
/// having gone past a working set of this size, what the entries describe is no longer the correspondents this mailbox
/// keeps meeting, and starting again costs one lookup per domain still in use.
/// </para>
/// <para>
/// An answer of "nothing is published" is cached too, and for a shorter time. Without it a domain whose selector was
/// retired would be asked for again on every message it ever sent; with a long one, a nameserver that was briefly
/// unreachable would go on being treated as silent long after it recovered.
/// </para>
/// <para>
/// Nothing here is personal data on its own — a selector and a signing domain are published so that anybody may ask for
/// them — but the set of domains a cache holds says who this mailbox corresponds with, so nothing in it is logged.
/// </para>
/// </remarks>
internal sealed class DkimPublicKeyRecordCache
{
    /// <summary>How many distinct selector-and-domain pairs are held before the cache starts over.</summary>
    private const int MaximumEntries = 2048;

    /// <summary>The shortest time an answer is held, whatever a shorter record time-to-live would ask for.</summary>
    /// <remarks>
    /// A domain publishing a very low time-to-live is describing how quickly it may move a record, not inviting one
    /// lookup per message. A minute keeps a synchronization run's worth of messages behind one query while leaving a
    /// key rotation visible well inside the interval anybody would notice it in.
    /// </remarks>
    private static readonly TimeSpan ShortestLifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest time an answer is held, whatever a longer record time-to-live would permit.</summary>
    private static readonly TimeSpan LongestLifetime = TimeSpan.FromDays(1);

    /// <summary>How long the absence of a published record is remembered.</summary>
    private static readonly TimeSpan AbsenceLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, CachedRecord> recordsByName = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes an empty cache over the clock its lifetimes are measured against.</summary>
    /// <param name="timeProvider">Supplies the current instant.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public DkimPublicKeyRecordCache(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Reads what is held for one name, when something still is.</summary>
    /// <param name="name">The fully qualified name the record is published at.</param>
    /// <param name="record">The record's text, or <see langword="null" /> where the cached answer is that none is published.</param>
    /// <returns><see langword="true" /> when an unexpired answer was held; otherwise <see langword="false" />.</returns>
    public bool TryRead(string name, out string? record)
    {
        record = null;

        if (!this.recordsByName.TryGetValue(name, out var cached)
            || cached.ExpiresAt <= this.timeProvider.GetUtcNow())
        {
            return false;
        }

        record = cached.Record;

        return true;
    }

    /// <summary>Holds a resolved record for as long as its own time-to-live allows.</summary>
    /// <param name="name">The fully qualified name the record is published at.</param>
    /// <param name="record">The record's text.</param>
    /// <param name="timeToLive">What the answering nameserver said the record is good for.</param>
    public void Store(string name, string record, TimeSpan timeToLive) =>
        this.Hold(name, record, Clamped(timeToLive));

    /// <summary>Holds the answer that a name publishes nothing this verification can use.</summary>
    /// <param name="name">The fully qualified name that was asked for.</param>
    public void StoreAbsence(string name) => this.Hold(name, record: null, AbsenceLifetime);

    private static TimeSpan Clamped(TimeSpan timeToLive) =>
        timeToLive < ShortestLifetime ? ShortestLifetime
        : timeToLive > LongestLifetime ? LongestLifetime
        : timeToLive;

    private void Hold(string name, string? record, TimeSpan lifetime)
    {
        if (this.recordsByName.Count >= MaximumEntries && !this.recordsByName.ContainsKey(name))
        {
            this.recordsByName.Clear();
        }

        this.recordsByName[name] = new CachedRecord(record, this.timeProvider.GetUtcNow() + lifetime);
    }

    /// <summary>One held answer, which may be that the name publishes nothing.</summary>
    private readonly record struct CachedRecord(string? Record, DateTimeOffset ExpiresAt);
}
