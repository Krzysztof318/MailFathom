// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>A controllable clock that also reports what the code under test is waiting on it for.</summary>
/// <remarks>
/// <para>
/// Moving virtual time is only half of driving a pipeline that waits: the other half is knowing whether the step just
/// taken has been acted on, because the work a due timer starts runs on the thread pool long after
/// <see cref="FakeTimeProvider.Advance" /> has returned. The two counters here are what makes that observable.
/// <see cref="ScheduledWaits" /> rises whenever a wait is armed on the clock, and <see cref="ElapsedWaits" /> whenever
/// one comes due, so a caller can hold the clock still until the execution has either finished or armed the wait it
/// will sit on next.
/// </para>
/// <para>
/// A periodic timer arms its next occurrence by firing, so its callback records both. An infinite due time arms
/// nothing and is therefore not counted, which is what keeps a disarmed timer out of the totals.
/// </para>
/// </remarks>
internal sealed class WaitRecordingTimeProvider : FakeTimeProvider
{
    private long scheduledWaits;
    private long elapsedWaits;

    /// <summary>Gets how many waits have been armed on this clock.</summary>
    internal long ScheduledWaits => Interlocked.Read(ref this.scheduledWaits);

    /// <summary>Gets how many armed waits have come due.</summary>
    internal long ElapsedWaits => Interlocked.Read(ref this.elapsedWaits);

    /// <summary>Gets how many armed waits are still ahead of the clock, so a caller can tell whether anything is waiting on it.</summary>
    internal long OutstandingWaits => this.ScheduledWaits - this.ElapsedWaits;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        this.RecordArmedWait(dueTime);

        return new RecordingTimer(
            base.CreateTimer(
                timerState =>
                {
                    Interlocked.Increment(ref this.elapsedWaits);
                    this.RecordArmedWait(period);
                    callback(timerState);
                },
                state,
                dueTime,
                period),
            this);
    }

    private void RecordArmedWait(TimeSpan dueTime)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            Interlocked.Increment(ref this.scheduledWaits);
        }
    }

    /// <summary>A timer that reports the waits it is re-armed with, since arming one is not always a creation.</summary>
    private sealed class RecordingTimer(ITimer timer, WaitRecordingTimeProvider clock) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            clock.RecordArmedWait(dueTime);

            return timer.Change(dueTime, period);
        }

        public void Dispose() => timer.Dispose();

        public ValueTask DisposeAsync() => timer.DisposeAsync();
    }
}
