// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>When the embedding upkeep pass is next due, and the one way an operator's act brings it forward.</summary>
/// <remarks>
/// <para>
/// The pause between passes is chosen by the pass that just ended, so nothing a later act writes can shorten it. An
/// instance that has activated no profile ends every pass having found no generation to walk towards, which is the long
/// idle interval; the activation that gives it one then commits a row a sleeping worker has no way to observe, and the
/// first vectors of a reindex arrive whenever that unrelated interval happens to expire. This is the signal that closes
/// that gap — the act that creates the work says so, and the worker waiting out the pause takes the next pass at once.
/// </para>
/// <para>
/// The instant is the other half of the same fact and is held here for the same reason: nothing else in the process
/// knows it. An operator reading a deployment during that pause sees no vectors, no provider call, and no log line, and
/// what tells a wait apart from a failure is being able to read when the wait ends.
/// </para>
/// <para>
/// One instance per process, because a pass is one thing the process does. It is deliberately not durable: what it
/// carries is a decision the running worker has already made, so a restart correctly forgets it and the first pass of
/// the new process schedules the next one.
/// </para>
/// </remarks>
public sealed class EmbeddingBackfillSchedule
{
    private readonly Lock gate = new();
    private readonly TimeProvider timeProvider;

    private TaskCompletionSource? waitingWorker;
    private bool passRequested;
    private bool passesRun = true;

    /// <summary>Initializes a schedule holding no pass.</summary>
    /// <param name="timeProvider">Dates the pause the worker is taking and the moment an act brings a pass forward to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public EmbeddingBackfillSchedule(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Gets when the next pass is due, or <see langword="null" /> while no pass has been scheduled.</summary>
    /// <remarks>
    /// <para>
    /// An instant already past is a pass that is due now — either running, or about to be taken by a worker that has
    /// finished waiting. The absence has two causes and neither is a fault: a process that has only just started shows
    /// it until its first pass schedules the next one, and a deployment whose walk is turned off shows it for good,
    /// because <see cref="NoPassWillRun" /> is what its worker reports instead of ever waiting.
    /// </para>
    /// <para>
    /// A nullable <see cref="DateTimeOffset" /> is wider than one atomic write, so the read holds the gate and every
    /// assignment below is made while already holding it. The setter does not take the gate itself, because both
    /// writers do so for the rest of what they change as well and a re-entrant acquisition would say the two facts were
    /// separable.
    /// </para>
    /// </remarks>
    public DateTimeOffset? NextPassDueAt
    {
        get
        {
            lock (this.gate)
            {
                return field;
            }
        }

        private set;
    }

    /// <summary>Reports that this process runs no pass at all, so nothing is scheduled and nothing can be asked for.</summary>
    /// <remarks>
    /// <para>
    /// Called by the worker in place of ever waiting, because whether the walk runs is a configuration value the worker
    /// reads and nothing here can see. Without it an activation would record a due instant on a deployment where no
    /// pass will ever be taken, and the status surface would report an overdue pass for as long as the process lived —
    /// which is the reading this whole type exists to prevent, arrived at from the other direction.
    /// </para>
    /// <para>
    /// It also clears a request that got in first, because an activation can reach the process before its worker has
    /// run far enough to say this. There is no way back: whether the walk runs is read once at startup, so a schedule
    /// told this keeps saying it until the process ends.
    /// </para>
    /// </remarks>
    public void NoPassWillRun()
    {
        lock (this.gate)
        {
            this.passesRun = false;
            this.passRequested = false;
            this.NextPassDueAt = null;
        }
    }

    /// <summary>Asks for a pass now, releasing a worker that is waiting out the pause the last pass chose.</summary>
    /// <remarks>
    /// Called by the act that made a pass worth running rather than by anything watching for one, because the state it
    /// reacts to is a row that act has just committed. A request arriving while a pass is already running is held in the
    /// flag rather than dropped, so work an operator asked for is never lost to a worker that was busy instead of
    /// waiting; a second request against a first nobody has taken yet asks for the same single pass. Where
    /// <see cref="NoPassWillRun" /> has been reported it does nothing at all, because there is no worker to release and
    /// recording a pass nothing will take would be a worse answer than recording none.
    /// </remarks>
    public void BringForward()
    {
        TaskCompletionSource? waiting;

        lock (this.gate)
        {
            if (!this.passesRun)
            {
                return;
            }

            this.NextPassDueAt = this.timeProvider.GetUtcNow();
            this.passRequested = true;
            waiting = this.waitingWorker;
            this.waitingWorker = null;
        }

        // Completed outside the lock, so nothing a released worker goes on to do runs while this one holds it.
        waiting?.TrySetResult();
    }

    /// <summary>Records that the next pass is due after the given pause, and waits for it.</summary>
    /// <param name="pause">How long the pass that just ended asked to wait.</param>
    /// <param name="cancellationToken">Ends the wait when the process is stopping.</param>
    /// <returns><see langword="true" /> when the wait ended because a pass was brought forward, <see langword="false" /> when the pause simply elapsed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// Recording and waiting are one call so that a pause cannot be taken without the instant that ends it being
    /// readable, which is the whole of what an operator is missing while a deployment sits quiet.
    /// </remarks>
    public async Task<bool> WaitForNextPassAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource broughtForward;

        lock (this.gate)
        {
            // Taken before the pause is recorded, because a request made while the last pass was running is a pass that
            // is already due: recording an instant for it would report a wait that is not going to happen.
            if (this.passRequested)
            {
                this.passRequested = false;

                return true;
            }

            this.NextPassDueAt = this.timeProvider.GetUtcNow() + pause;

            // Asynchronous continuations, so completing this never runs the worker's next pass on the thread of the
            // request that activated a profile.
            broughtForward = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.waitingWorker = broughtForward;
        }

        using var pauseEnded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var elapsing = Task.Delay(pause, this.timeProvider, pauseEnded.Token);
        var ended = await Task.WhenAny(broughtForward.Task, elapsing);

        lock (this.gate)
        {
            this.waitingWorker = null;
            this.passRequested = false;
        }

        if (ended == elapsing)
        {
            // Awaited rather than discarded, so a stopping process ends the loop here instead of taking one more pass.
            await elapsing;

            return false;
        }

        // Ends the timer the pause created, which would otherwise outlive a wait it no longer bounds.
        await pauseEnded.CancelAsync();

        return true;
    }
}
