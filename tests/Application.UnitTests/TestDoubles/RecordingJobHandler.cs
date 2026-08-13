// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A job handler that records what it was asked to run and does whatever the test told it to.</summary>
/// <remarks>
/// Hand-written rather than substituted because the tests read what a handler received and how far it got, which is
/// state rather than an interaction, and because several of them need the handler to block until its token is
/// cancelled — the shape a substitute expresses least readably.
/// </remarks>
internal sealed class RecordingJobHandler : IJobHandler
{
    private readonly Func<IJobPayload, CancellationToken, Task> work;
    private int runCount;

    internal RecordingJobHandler(JobType jobType, Func<IJobPayload, CancellationToken, Task>? work = null)
    {
        this.JobType = jobType;
        this.work = work ?? ((_, _) => Task.CompletedTask);
    }

    /// <inheritdoc />
    public JobType JobType { get; }

    /// <summary>Gets the payload the handler was last handed, and <see langword="null" /> while it has been handed none.</summary>
    internal IJobPayload? ReceivedPayload { get; private set; }

    /// <summary>Gets how many times the handler was run.</summary>
    /// <remarks>Counted atomically, because a pass runs the jobs of one batch at once and a handler serves all of them.</remarks>
    internal int RunCount => Volatile.Read(ref this.runCount);

    /// <inheritdoc />
    public Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        this.ReceivedPayload = payload;
        Interlocked.Increment(ref this.runCount);

        return this.work(payload, cancellationToken);
    }

    /// <summary>Builds work that reports it started and then waits for the token, which is how a slow job is modelled.</summary>
    /// <param name="started">Completed the moment the handler begins.</param>
    /// <returns>Work that ends only when its token is cancelled.</returns>
    /// <remarks>
    /// The wait is a registration on the token rather than a delay, so a test that advances its clock before the
    /// handler reached this line still cancels it: registering on an already-cancelled token runs the callback at once,
    /// which is what makes the arrangement free of a race the test would otherwise have to sleep through.
    /// </remarks>
    internal static Func<IJobPayload, CancellationToken, Task> BlockUntilCancelled(TaskCompletionSource started) =>
        async (_, cancellationToken) =>
        {
            started.TrySetResult();

            var blocked = new TaskCompletionSource();

            await using var registration = cancellationToken.Register(() => blocked.TrySetCanceled(cancellationToken));

            await blocked.Task;
        };
}
