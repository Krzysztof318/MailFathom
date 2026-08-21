// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Jobs.Execution;

namespace MailFathom.Host.Configuration.Jobs;

/// <summary>Configures the queue of durable background work: how much may be waiting in it, and the worker that runs it.</summary>
/// <remarks>
/// <para>
/// Every setting here is a bound on the queue rather than on any consumer of it: how much one pass takes, how long it
/// holds it, how long one job may run, how much runs at once, how much may be waiting, and how often the worker looks
/// again when the queue was empty. What a job actually does is the consumer's own configuration, which is why nothing
/// here names a job type.
/// </para>
/// <para>
/// <see cref="MaxQueueDepthPerType" /> is the one setting that still applies with <see cref="Enabled" /> switched off,
/// because it bounds enqueuing rather than running: a replica serving MCP reads and nothing else still refuses to grow
/// a backlog past what the deployment accepts.
/// </para>
/// <para>
/// Two rules an attribute cannot express live in the validator below. An attempt has to be cancelled before its lease
/// can expire underneath it, which is what keeps two workers off one job; and a per-type concurrency ceiling above the
/// process ceiling would state a bound nothing can reach. A deployment that gets either wrong fails to start rather than
/// running with the guarantee quietly gone.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class JobQueueOptions : IValidatableObject
{
    /// <summary>Gets or sets whether the worker runs.</summary>
    /// <remarks>
    /// On by default, and on an instance with no registered handler it costs nothing: the worker says so once and stops,
    /// because a claim filtered to no job type would take work this build cannot run. Turning it off is for an operator
    /// who wants a replica serving MCP reads and nothing else.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the greatest number of jobs one pass claims.</summary>
    /// <remarks>
    /// It bounds what one claim takes, not what runs: a claimed job waits for a concurrency slot like any other, so
    /// raising this makes a busy queue drain with fewer round trips rather than making the instance work faster.
    /// </remarks>
    [Range(1, 100)]
    public int BatchSize { get; set; } = 5;

    /// <summary>Gets or sets how many jobs this instance runs at once, across every job type together.</summary>
    /// <remarks>
    /// This is the bound that decides how much of the instance background work may take, and it is expressed here rather
    /// than left to the database connection pool: a limit that emerged from how many connections happened to be
    /// available would move whenever anything else in the process opened one, and would surface as a query waiting on a
    /// pool rather than as a job waiting for its turn. The range keeps it comfortably below the pool a stock connection
    /// string provides for the same reason.
    /// </remarks>
    [Range(1, 32)]
    public int MaxConcurrentJobs { get; set; } = 4;

    /// <summary>Gets or sets how many jobs of one type this instance runs at once.</summary>
    /// <remarks>
    /// One consumer flooding the queue is the ordinary case, so a shared ceiling alone would let a bulk re-evaluation of
    /// one kind of work be the reason another kind never runs. A job waiting on this ceiling occupies none of
    /// <see cref="MaxConcurrentJobs" />, which is what leaves that room for another type. Must not exceed it.
    /// </remarks>
    [Range(1, 32)]
    public int MaxConcurrentJobsPerType { get; set; } = 2;

    /// <summary>Gets or sets how many jobs of one type may be waiting before enqueuing is refused.</summary>
    /// <remarks>
    /// Reaching it makes enqueuing report backpressure to whoever asked, which is a described outcome rather than a
    /// failure: the work was neither queued nor lost, and the caller slows down or asks again later. Refusing per type
    /// is what keeps a consumer that filled its own queue from stopping every other consumer, and a request whose work
    /// is already queued is answered with that job rather than refused.
    /// </remarks>
    [Range(1, 1000000)]
    public int MaxQueueDepthPerType { get; set; } = 10000;

    /// <summary>Gets or sets how long a claim holds each job it takes.</summary>
    /// <remarks>
    /// It is how long work stays held after the process running it stops existing, so it is the delay before a crash is
    /// recovered from rather than a bound on anything a healthy instance does — a running attempt renews it at half this
    /// value for as long as it works.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:02", "01:00:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets how long one job may run before it is cancelled.</summary>
    /// <remarks>Must be shorter than <see cref="LeaseDuration" />, which is what stops an attempt from outliving the lease it holds.</remarks>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets how many attempts one job may be handed out for before a transient failure ends it.</summary>
    /// <remarks>
    /// It bounds a transient failure and nothing else: a permanent one is terminal on its first attempt, because
    /// spending the budget to reach an answer already known would hold a worker for nothing. Setting it to <c>1</c>
    /// leaves no retry at all, which is a deployment saying every failure is somebody's to look at.
    /// </remarks>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Gets or sets the delay the first retry is drawn around, from which the doubling grows.</summary>
    /// <remarks>
    /// A delay is drawn from a range rather than computed exactly, because jobs that failed together failed on the same
    /// dependency and an exact delay would return all of them to it in the same instant.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the ceiling a grown retry delay never exceeds.</summary>
    /// <remarks>Must be at least <see cref="RetryBaseDelay" />, which is what keeps the growth from being capped below where it starts.</remarks>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00")]
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets how long the worker waits before looking again when a pass found nothing due.</summary>
    /// <remarks>
    /// A pass that filled its batch looks again at once, because a queue with work in it should be drained rather than
    /// polled. This is only how long an idle instance waits, so it trades how quickly enqueued work starts against how
    /// often an empty queue is queried.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Reads the six keys one attempt at a job is bounded by.</summary>
    /// <returns>The settings the worker claims, retries, and gives up under.</returns>
    /// <remarks>Create refuses an inverted pair, which the options validator has already rejected at startup.</remarks>
    internal JobExecutionSettings ToExecutionSettings() => JobExecutionSettings.Create(
        this.BatchSize,
        this.LeaseDuration,
        this.ExecutionTimeout,
        this.MaxAttempts,
        this.RetryBaseDelay,
        this.RetryMaxDelay);

    /// <summary>Reads the three keys that say how much of this instance background work may take.</summary>
    /// <returns>The capacity the gate hands out.</returns>
    internal JobCapacitySettings ToCapacitySettings() => JobCapacitySettings.Create(
        this.MaxConcurrentJobs,
        this.MaxConcurrentJobsPerType,
        this.MaxQueueDepthPerType);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.ExecutionTimeout >= this.LeaseDuration)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Jobs:ExecutionTimeout must be shorter than Jobs:LeaseDuration, which is {0}. A job allowed to run for as long as its lease is held can be claimed by a second worker while the first is still running it.",
                    this.LeaseDuration),
                [nameof(this.ExecutionTimeout)]);
        }

        if (this.MaxConcurrentJobsPerType > this.MaxConcurrentJobs)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Jobs:MaxConcurrentJobsPerType must not exceed Jobs:MaxConcurrentJobs, which is {0}. A per-type ceiling above the ceiling for the whole instance can never be reached, so it states a bound nobody has.",
                    this.MaxConcurrentJobs),
                [nameof(this.MaxConcurrentJobsPerType)]);
        }

        if (this.RetryMaxDelay < this.RetryBaseDelay)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Jobs:RetryMaxDelay must be at least Jobs:RetryBaseDelay, which is {0}. A ceiling below the delay the growth starts from caps every retry at the ceiling and leaves the backoff with nothing to grow.",
                    this.RetryBaseDelay),
                [nameof(this.RetryMaxDelay)]);
        }
    }
}
