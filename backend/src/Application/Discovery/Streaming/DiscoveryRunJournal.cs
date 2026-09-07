// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.CompilerServices;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Everything one run has published, in order, readable from any point by anyone holding the run.</summary>
/// <remarks>
/// <para>
/// A run outlives the connection that started it, which is the whole reason this exists. The events are kept rather
/// than pushed, so a client that lost its network reattaches and is given what it missed, and a client that never comes
/// back costs nothing but the events themselves until the run is forgotten.
/// </para>
/// <para>
/// <strong>Nothing is ever evicted from it.</strong> A run publishes at most
/// <see cref="DiscoveryRunBounds.MaximumEvents" /> events, which is what the presentation contract can express, so the
/// whole of a run fits and resumption is exact rather than best-effort — there is no state in which a client asks for
/// what it missed and is told the run has moved on. Reaching the bound stops the run publishing anything but its
/// ending, which <see cref="Append" /> reports so the run can say it composed less than it found.
/// </para>
/// <para>
/// A slow reader never slows the run down either. Publishing appends and returns; reading is a separate walk over what
/// has been appended, and two clients reading at different speeds see the same events in the same order.
/// </para>
/// <para>
/// The user is carried because what a run holds is that person's mail. Whoever looks a run up is checked against it,
/// which is what makes a guessed identifier useless rather than merely unlikely.
/// </para>
/// <para>
/// <strong>It also carries the token a stop arrives on.</strong> A run outlives every connection it is reached over, so
/// the execution has to be reachable from a later request, and the journal is the one object per run that both the
/// execution and that request already hold. What owns the cancellation is whatever holds the run — see
/// <see cref="DiscoveryRunRegistry" /> — because that is what knows when nothing can stop the run any more.
/// </para>
/// </remarks>
public sealed class DiscoveryRunJournal
{
    private readonly object mutex = new();
    private readonly List<DiscoveryRunEvent> published = [];

    private TaskCompletionSource appended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool ended;

    /// <summary>Initializes the stream of one run.</summary>
    /// <param name="id">The identifier the run is addressed by.</param>
    /// <param name="user">Whose mail the run reads, which is who may read what it publishes.</param>
    /// <param name="stopping">Cancelled when the run is stopped, which is what the execution links its own cancellation to.</param>
    public DiscoveryRunJournal(DiscoveryRunId id, MailUserId user, CancellationToken stopping = default)
    {
        this.Id = id;
        this.User = user;
        this.Stopping = stopping;
    }

    /// <summary>Gets the identifier the run is addressed by.</summary>
    public DiscoveryRunId Id { get; }

    /// <summary>Gets whose mail the run reads.</summary>
    public MailUserId User { get; }

    /// <summary>Gets whether the run has published its ending, after which nothing further is appended.</summary>
    public bool HasEnded
    {
        get
        {
            lock (this.mutex)
            {
                return this.ended;
            }
        }
    }

    /// <summary>Gets the token a stop reaches the execution through, which the run links its own cancellation to.</summary>
    /// <remarks>Never cancelled where the run was opened without one, which is a run nothing can stop — the shape a test states when a stop is not what it is about.</remarks>
    public CancellationToken Stopping { get; }

    /// <summary>Gets how many events the run has published.</summary>
    public int PublishedCount
    {
        get
        {
            lock (this.mutex)
            {
                return this.published.Count;
            }
        }
    }

    /// <summary>Publishes one event, stamping it with the run and its place in it.</summary>
    /// <param name="event">What happened, carrying neither a run nor a sequence of its own.</param>
    /// <returns><see langword="true" /> when the event was published; <see langword="false" /> when the run has already ended or has no room left for anything but its ending.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An ending is always published while the run has not already ended, because a bound that could swallow one would
    /// leave a client waiting on a run that had stopped. Everything else is refused once the reservation for it is all
    /// that remains, and the caller ends the run rather than retrying.
    /// </remarks>
    public bool Append(DiscoveryRunEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        TaskCompletionSource waiting;

        lock (this.mutex)
        {
            if (this.ended)
            {
                return false;
            }

            if (!@event.EndsTheRun && this.published.Count >= DiscoveryRunBounds.MaximumEvents - 1)
            {
                return false;
            }

            this.published.Add(@event with { RunId = this.Id, Sequence = this.published.Count + 1 });
            this.ended = @event.EndsTheRun;

            waiting = this.appended;
            this.appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        waiting.SetResult();

        return true;
    }

    /// <summary>Reads the run from a stated point, waiting for what has not happened yet.</summary>
    /// <param name="afterSequence">The last sequence the caller already holds, or <c>0</c> to read the run from its beginning.</param>
    /// <param name="cancellationToken">Ends the read where the caller stops listening.</param>
    /// <returns>Every event after that point, in order, ending when the run does.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="afterSequence" /> is negative.</exception>
    /// <remarks>
    /// A sequence this run has not reached is read as the beginning rather than as a point to wait at. No client can
    /// hold one honestly — a sequence is only ever learned by being sent it — so what such a value means is a client
    /// whose state does not belong to this run, and replaying it costs a few events where waiting would hand back a run
    /// missing everything before the number. The walk ends on the run's own ending rather than on the caller's
    /// judgement, so nothing has to decide from outside whether more is coming.
    /// </remarks>
    public async IAsyncEnumerable<DiscoveryRunEvent> ReadFromAsync(
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var read = afterSequence > this.PublishedCount ? 0 : afterSequence;

        while (true)
        {
            var (batch, endedAfterBatch, nextAppend) = this.Since(read);

            foreach (var @event in batch)
            {
                read = @event.Sequence;

                yield return @event;
            }

            if (endedAfterBatch)
            {
                yield break;
            }

            await nextAppend.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Takes what has been published past a point, with the signal that fires when more is.</summary>
    /// <remarks>
    /// The signal is taken inside the same lock as the batch, so an event appended between the two is awaited rather
    /// than missed — the completion source a reader waits on is the one that was current when it read.
    /// </remarks>
    private (IReadOnlyList<DiscoveryRunEvent> Batch, bool Ended, Task NextAppend) Since(long afterSequence)
    {
        lock (this.mutex)
        {
            IReadOnlyList<DiscoveryRunEvent> taken = afterSequence >= this.published.Count
                ? []
                : [.. this.published.Skip((int)afterSequence)];

            return (taken, this.ended, this.appended.Task);
        }
    }
}
