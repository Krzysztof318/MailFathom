// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Asks for work this process can actually run, and says under what lease it would hold it.</summary>
/// <remarks>
/// <para>
/// The claim is filtered to the types the asking process has a handler for, which is what makes a rolling deployment
/// safe: a job whose type an older replica does not know is left where it is for a newer one to take, because the
/// absence of a handler is a fact about the deployment and not about the work.
/// </para>
/// <para>
/// The lease duration is the caller's because the bound belongs with the timeout the work runs under, and the two are
/// ordered against each other — an attempt is cancelled before its lease can expire underneath it. Nothing here
/// enforces that ordering; it is enforced where both values are configured.
/// </para>
/// </remarks>
public sealed record JobClaimRequest
{
    private JobClaimRequest(
        IReadOnlyList<JobType> handledTypes,
        int batchSize,
        TimeSpan leaseDuration,
        JobLeaseOwner owner)
    {
        this.HandledTypes = handledTypes;
        this.BatchSize = batchSize;
        this.LeaseDuration = leaseDuration;
        this.Owner = owner;
    }

    /// <summary>Gets the job types this process has a handler for.</summary>
    public IReadOnlyList<JobType> HandledTypes { get; }

    /// <summary>Gets the greatest number of jobs this claim may take.</summary>
    public int BatchSize { get; }

    /// <summary>Gets how long the claim holds each job it takes.</summary>
    public TimeSpan LeaseDuration { get; }

    /// <summary>Gets the attempt the claimed jobs are stamped with.</summary>
    public JobLeaseOwner Owner { get; }

    /// <summary>States what this process can run and how long it would hold it for.</summary>
    /// <param name="handledTypes">The job types this process has a handler for.</param>
    /// <param name="batchSize">The greatest number of jobs to take.</param>
    /// <param name="leaseDuration">How long each claimed job is held.</param>
    /// <param name="owner">The attempt the claimed jobs are stamped with.</param>
    /// <returns>The validated claim.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="handledTypes" /> or <paramref name="owner" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="handledTypes" /> is empty, repeats a type, or holds the unspecified default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive or <paramref name="leaseDuration" /> is not positive.</exception>
    public static JobClaimRequest Create(
        IReadOnlyList<JobType> handledTypes,
        int batchSize,
        TimeSpan leaseDuration,
        JobLeaseOwner owner)
    {
        ArgumentNullException.ThrowIfNull(handledTypes);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        if (handledTypes.Count == 0)
        {
            throw new ArgumentException(
                "A claim names at least one job type this process can run.",
                nameof(handledTypes));
        }

        if (handledTypes.Any(handledType => !handledType.IsSpecified))
        {
            throw new ArgumentException("A claim names declared job types.", nameof(handledTypes));
        }

        // A repeated type would widen no predicate and hide a registration mistake, so it is refused rather than
        // collapsed: a process registering one handler twice has a defect the claim should not absorb.
        if (handledTypes.Distinct().Count() != handledTypes.Count)
        {
            throw new ArgumentException("A claim names each job type once.", nameof(handledTypes));
        }

        return new JobClaimRequest([.. handledTypes], batchSize, leaseDuration, owner);
    }
}
