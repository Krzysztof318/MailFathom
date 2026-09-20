// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Streaming;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Holds Discover runs and what they wrote in memory, so a test has somewhere for a run to be durable.</summary>
/// <remarks>
/// <para>
/// Hand-written rather than substituted, because what these tests assert is the behaviour the statements settle: that a
/// sequence starts at one and never skips, that a cursor hands back only the tail, that a stop refuses the next write,
/// and that a person's ninth concurrent run is refused. A substitute would answer from a script and the assertion would
/// be about the script.
/// </para>
/// <para>
/// <strong>One instance is one deployment rather than one replica</strong>, which is the whole reason a run is durable:
/// a test that reads a run through one collaborator and writes it through another, over this one store, is a client
/// reaching a replica that did not start the run.
/// </para>
/// <para>
/// It reproduces every refusal the real statements take and none of their concurrency: nothing here runs two writers at
/// once, because a run is written by one execution and the key over the run and the sequence is what settles the rest.
/// The retention windows are the run's own bounds, measured against whatever instant the caller states.
/// </para>
/// </remarks>
internal sealed class InMemoryDiscoveryRunStore : IDiscoveryRunStore
{
    private readonly Dictionary<DiscoveryRunId, HeldRun> runs = [];

    /// <summary>Gets how many runs the store is holding, ended or not.</summary>
    public int HeldCount => this.runs.Count;

    /// <summary>Reports whether the store was asked to stop a run, which is what an executing replica meets as a refusal.</summary>
    /// <param name="id">The run to read.</param>
    /// <returns><see langword="true" /> when a stop was recorded against it.</returns>
    public bool WasAskedToStop(DiscoveryRunId id) =>
        this.runs.TryGetValue(id, out var held) && held.StopRequested;

    /// <summary>Reads everything one run has written, in order, without moving its retention window.</summary>
    /// <param name="id">The run to read.</param>
    /// <returns>The events, or an empty sequence where the store holds no such run.</returns>
    public IReadOnlyList<DiscoveryRunEvent> Written(DiscoveryRunId id) =>
        this.runs.TryGetValue(id, out var held) ? held.Events : [];

    /// <inheritdoc />
    public Task<bool> TryOpenAsync(
        DiscoveryRunId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.Forget(now);

        var running = this.runs.Values.Count(held => held.User == user && held.EndedAt is null);

        if (running >= DiscoveryRunBounds.MaximumConcurrentRunsPerUser)
        {
            return Task.FromResult(false);
        }

        this.runs[id] = new HeldRun(user, now, now);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<long?> AppendAsync(
        DiscoveryRunId id,
        DiscoveryRunEvent written,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(written);
        cancellationToken.ThrowIfCancellationRequested();

        if (!this.runs.TryGetValue(id, out var held) || held.EndedAt is not null)
        {
            return Task.FromResult<long?>(null);
        }

        if (!written.EndsTheRun
            && (held.StopRequested || held.Events.Count >= DiscoveryRunBounds.MaximumEvents - 1))
        {
            return Task.FromResult<long?>(null);
        }

        var sequence = held.Events.Count + 1L;
        held.Events.Add(written with { RunId = id, Sequence = sequence });
        this.runs[id] = held with
        {
            LastUsedAt = now,
            EndedAt = written.EndsTheRun ? now : held.EndedAt,
        };

        return Task.FromResult<long?>(sequence);
    }

    /// <inheritdoc />
    public Task<DiscoveryRunReading?> ReadAsync(
        DiscoveryRunId id,
        MailUserId user,
        long afterSequence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        cancellationToken.ThrowIfCancellationRequested();

        if (!this.runs.TryGetValue(id, out var held) || held.User != user)
        {
            return Task.FromResult<DiscoveryRunReading?>(null);
        }

        this.runs[id] = held with { LastUsedAt = now };

        var from = afterSequence > held.Events.Count ? 0 : afterSequence;

        return Task.FromResult<DiscoveryRunReading?>(new DiscoveryRunReading(
            held.EndedAt is null,
            [.. held.Events.Skip((int)from)]));
    }

    /// <inheritdoc />
    public Task<bool> TryRequestStopAsync(
        DiscoveryRunId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!this.runs.TryGetValue(id, out var held) || held.User != user)
        {
            return Task.FromResult(false);
        }

        this.runs[id] = held with
        {
            LastUsedAt = now,
            StopRequested = held.StopRequested || held.EndedAt is null,
        };

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int> RemoveForgottenAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.Forget(now));
    }

    private int Forget(DateTimeOffset now)
    {
        var forgettableFrom = now - DiscoveryRunBounds.RetentionAfterLastUse;
        var unreportedFrom = now - (DiscoveryRunBounds.MaximumDuration + DiscoveryRunBounds.RetentionAfterLastUse);

        var forgotten = this.runs
            .Where(entry => (entry.Value.EndedAt is not null && entry.Value.LastUsedAt <= forgettableFrom)
                || entry.Value.StartedAt <= unreportedFrom)
            .Select(static entry => entry.Key)
            .ToArray();

        return forgotten.Count(this.runs.Remove);
    }

    private sealed record HeldRun(MailUserId User, DateTimeOffset StartedAt, DateTimeOffset LastUsedAt)
    {
        public List<DiscoveryRunEvent> Events { get; } = [];

        public DateTimeOffset? EndedAt { get; init; }

        public bool StopRequested { get; init; }
    }
}
