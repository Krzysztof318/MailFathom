// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Bounds one pass of the worker: how much it takes, how long it holds it, how long one job may run, and how often a transient failure is retried.</summary>
/// <remarks>
/// <para>
/// The ordering between the two durations is the whole reason this is validated rather than passed as loose values. An
/// attempt has to be cancelled before its lease can expire underneath it, because a lease that ran out while its holder
/// was still working is a second worker taking the same job — so a timeout at or above the lease duration is refused
/// rather than warned about.
/// </para>
/// <para>
/// The renewal interval is derived rather than configured, at half the lease. A third duration would be a third way for
/// the three to disagree, and half a lease is the margin the ordering above needs: a renewal that fails once still
/// leaves a whole half-lease before anything can reclaim the job.
/// </para>
/// <para>
/// The attempt bound and the two retry delays are the queue's own budget for repeating a job, and there is exactly one
/// of them. A handler does not bring its own: what it calls is already retried inside one attempt by the resilience
/// pipeline of the dependency it reached, and a second bound at this level would multiply against that one instead of
/// bounding anything.
/// </para>
/// </remarks>
public sealed record JobExecutionSettings
{
    private JobExecutionSettings(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan executionTimeout,
        int maxAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan retryMaxDelay)
    {
        this.BatchSize = batchSize;
        this.LeaseDuration = leaseDuration;
        this.ExecutionTimeout = executionTimeout;
        this.MaxAttempts = maxAttempts;
        this.RetryBaseDelay = retryBaseDelay;
        this.RetryMaxDelay = retryMaxDelay;
    }

    /// <summary>Gets the greatest number of jobs one pass takes.</summary>
    public int BatchSize { get; }

    /// <summary>Gets how long a claim holds each job it takes.</summary>
    public TimeSpan LeaseDuration { get; }

    /// <summary>Gets how long one job may run before it is cancelled.</summary>
    public TimeSpan ExecutionTimeout { get; }

    /// <summary>Gets how many attempts one job may be handed out for before a transient failure dead-letters it.</summary>
    /// <remarks>A value of <c>1</c> leaves no retry at all, so the first failure of any classification is terminal.</remarks>
    public int MaxAttempts { get; }

    /// <summary>Gets the delay the first retry is drawn around, from which the doubling grows.</summary>
    public TimeSpan RetryBaseDelay { get; }

    /// <summary>Gets the ceiling a grown retry delay never exceeds.</summary>
    public TimeSpan RetryMaxDelay { get; }

    /// <summary>Gets how often a held job's lease is pushed further out while its handler works.</summary>
    public TimeSpan LeaseRenewalInterval => this.LeaseDuration / 2;

    /// <summary>States the bounds one pass runs under.</summary>
    /// <param name="batchSize">The greatest number of jobs one pass takes.</param>
    /// <param name="leaseDuration">How long a claim holds each job it takes.</param>
    /// <param name="executionTimeout">How long one job may run before it is cancelled.</param>
    /// <param name="maxAttempts">How many attempts one job may be handed out for.</param>
    /// <param name="retryBaseDelay">The delay the first retry is drawn around.</param>
    /// <param name="retryMaxDelay">The ceiling a grown retry delay never exceeds.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> or <paramref name="maxAttempts" /> is not positive, when a duration is not positive, when <paramref name="executionTimeout" /> is not shorter than <paramref name="leaseDuration" />, or when <paramref name="retryMaxDelay" /> is below <paramref name="retryBaseDelay" />.</exception>
    public static JobExecutionSettings Create(
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan executionTimeout,
        int maxAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan retryMaxDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(executionTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(executionTimeout, leaseDuration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryBaseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryMaxDelay, retryBaseDelay);

        return new JobExecutionSettings(
            batchSize,
            leaseDuration,
            executionTimeout,
            maxAttempts,
            retryBaseDelay,
            retryMaxDelay);
    }
}
