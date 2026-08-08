// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Bounds how many raw MIME bytes every concurrent work unit of the process may hold in memory at once.</summary>
/// <remarks>
/// <para>
/// A payload is buffered whole between the fetch that reads it and the commit that stores it, so peak memory is one
/// payload per work unit in flight. The per-email size limit bounds one of those and says nothing about their sum,
/// which means the peak would otherwise scale with the configured account and folder concurrency: raising a
/// concurrency bound would silently raise the memory ceiling with it. This is the one budget that spans work units, so
/// it is a single process-wide instance rather than a per-run value.
/// </para>
/// <para>
/// Reservations are granted in request order, and a request is granted only once every earlier one has been. That is
/// what keeps a large message from being starved by a stream of small ones, and it is why the capacity must be at
/// least the per-email size limit: a reservation larger than the whole budget could never be granted, and the work
/// unit asking for it would wait forever. The host validates that at startup, and this type refuses such a request
/// outright rather than waiting on it.
/// </para>
/// <para>
/// The budget bounds what MailFathom deliberately holds rather than what the runtime has allocated. A reservation is
/// the caller's statement that it is about to hold that many bytes, so it is taken before the fetch and released only
/// once nothing references the payload any more.
/// </para>
/// </remarks>
public sealed class RawMimeMemoryBudget
{
    private readonly Lock gate = new();
    private readonly Queue<PendingReservation> waiting = new();
    private long availableBytes;

    /// <summary>Initializes a budget over a fixed number of bytes.</summary>
    /// <param name="capacityBytes">The greatest number of raw MIME bytes every work unit together may hold.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacityBytes" /> is not positive.</exception>
    public RawMimeMemoryBudget(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);

        this.CapacityBytes = capacityBytes;
        this.availableBytes = capacityBytes;
    }

    /// <summary>Gets the greatest number of raw MIME bytes every work unit together may hold.</summary>
    public long CapacityBytes { get; }

    /// <summary>Gets how many bytes of the budget nothing currently holds.</summary>
    public long AvailableBytes
    {
        get
        {
            lock (this.gate)
            {
                return this.availableBytes;
            }
        }
    }

    /// <summary>Reserves room for one payload, waiting until the budget has it.</summary>
    /// <param name="bytes">How many bytes the caller is about to hold.</param>
    /// <param name="cancellationToken">Abandons the wait; a reservation already granted is unaffected.</param>
    /// <returns>The reservation, which returns its bytes to the budget when it is disposed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="bytes" /> is not positive or exceeds <see cref="CapacityBytes" />, which no wait
    /// could ever satisfy.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancelled before the budget had room.</exception>
    public async Task<RawMimeMemoryReservation> ReserveAsync(long bytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes, this.CapacityBytes);
        cancellationToken.ThrowIfCancellationRequested();

        PendingReservation pending;

        lock (this.gate)
        {
            if (this.waiting.Count == 0 && this.availableBytes >= bytes)
            {
                this.availableBytes -= bytes;

                return new RawMimeMemoryReservation(this, bytes);
            }

            pending = new PendingReservation(bytes);
            this.waiting.Enqueue(pending);
        }

        await using var registration = cancellationToken.Register(
            static state =>
            {
                var (budget, abandoned, token) = ((RawMimeMemoryBudget, PendingReservation, CancellationToken))state!;
                budget.Abandon(abandoned, token);
            },
            (this, pending, cancellationToken));

        return await pending.Completion.Task;
    }

    /// <summary>Returns a granted reservation's bytes and grants whatever the freed room now covers.</summary>
    /// <remarks>
    /// Waiting callers are completed outside the lock, because a continuation that ran inline would otherwise reserve
    /// again while this thread still held it.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of each granted reservation passes to the caller that was waiting for it, which disposes it when its payload is released.")]
    internal void Release(long bytes)
    {
        List<PendingReservation> granted = [];

        lock (this.gate)
        {
            this.availableBytes += bytes;

            while (this.waiting.TryPeek(out var next))
            {
                if (next.IsAbandoned)
                {
                    this.waiting.Dequeue();

                    continue;
                }

                if (next.Bytes > this.availableBytes)
                {
                    break;
                }

                this.waiting.Dequeue();
                next.MarkSettled();
                this.availableBytes -= next.Bytes;
                granted.Add(next);
            }
        }

        foreach (var reservation in granted)
        {
            reservation.Completion.SetResult(new RawMimeMemoryReservation(this, reservation.Bytes));
        }
    }

    /// <summary>Drops one waiting request, so a release skips it instead of reserving bytes for nobody.</summary>
    private void Abandon(PendingReservation pending, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            if (!pending.TryAbandon())
            {
                return;
            }
        }

        pending.Completion.TrySetCanceled(cancellationToken);
    }

    /// <summary>One request waiting for room, settled exactly once by a grant or by the caller's cancellation.</summary>
    /// <remarks>
    /// Both outcomes are decided under the budget's lock, which is what makes the settlement single: a request the
    /// release loop has taken out of the queue can no longer be abandoned, and an abandoned one is never granted.
    /// Without that, a cancellation racing a grant would leave reserved bytes that nobody holds and nobody releases.
    /// </remarks>
    private sealed class PendingReservation(long bytes)
    {
        private bool isSettled;

        public TaskCompletionSource<RawMimeMemoryReservation> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long Bytes { get; } = bytes;

        public bool IsAbandoned { get; private set; }

        /// <summary>Records that a release has taken this request out of the queue. Called under the budget's lock.</summary>
        public void MarkSettled() => this.isSettled = true;

        /// <summary>Abandons the request unless it has already been settled. Called under the budget's lock.</summary>
        /// <returns><see langword="true" /> when this call abandoned it.</returns>
        public bool TryAbandon()
        {
            if (this.isSettled)
            {
                return false;
            }

            this.isSettled = true;
            this.IsAbandoned = true;

            return true;
        }
    }
}
