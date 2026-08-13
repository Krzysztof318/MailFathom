// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Bounds one pass of the worker: how much it takes, how long it holds it, and how long one job may run.</summary>
/// <remarks>
/// <para>
/// The ordering between the two durations is the whole reason this is validated rather than passed as three loose
/// values. An attempt has to be cancelled before its lease can expire underneath it, because a lease that ran out while
/// its holder was still working is a second worker taking the same job — so a timeout at or above the lease duration is
/// refused rather than warned about.
/// </para>
/// <para>
/// The renewal interval is derived rather than configured, at half the lease. A third duration would be a third way for
/// the three to disagree, and half a lease is the margin the ordering above needs: a renewal that fails once still
/// leaves a whole half-lease before anything can reclaim the job.
/// </para>
/// </remarks>
public sealed record JobExecutionSettings
{
    private JobExecutionSettings(int batchSize, TimeSpan leaseDuration, TimeSpan executionTimeout)
    {
        this.BatchSize = batchSize;
        this.LeaseDuration = leaseDuration;
        this.ExecutionTimeout = executionTimeout;
    }

    /// <summary>Gets the greatest number of jobs one pass takes.</summary>
    public int BatchSize { get; }

    /// <summary>Gets how long a claim holds each job it takes.</summary>
    public TimeSpan LeaseDuration { get; }

    /// <summary>Gets how long one job may run before it is cancelled.</summary>
    public TimeSpan ExecutionTimeout { get; }

    /// <summary>Gets how often a held job's lease is pushed further out while its handler works.</summary>
    public TimeSpan LeaseRenewalInterval => this.LeaseDuration / 2;

    /// <summary>States the bounds one pass runs under.</summary>
    /// <param name="batchSize">The greatest number of jobs one pass takes.</param>
    /// <param name="leaseDuration">How long a claim holds each job it takes.</param>
    /// <param name="executionTimeout">How long one job may run before it is cancelled.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive, when either duration is not positive, or when <paramref name="executionTimeout" /> is not shorter than <paramref name="leaseDuration" />.</exception>
    public static JobExecutionSettings Create(int batchSize, TimeSpan leaseDuration, TimeSpan executionTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(executionTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(executionTimeout, leaseDuration);

        return new JobExecutionSettings(batchSize, leaseDuration, executionTimeout);
    }
}
