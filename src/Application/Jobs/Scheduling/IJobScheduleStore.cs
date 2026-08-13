// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>Keeps what each recurring dispatch has already done, so an occasion survives the process that noticed it.</summary>
/// <remarks>
/// <para>
/// No method here takes a persistence session, for the reason <see cref="IJobStore" /> takes none: a schedule is
/// advanced against work that is already enqueued, so there is nothing for a caller to enlist this in.
/// </para>
/// <para>
/// Two replicas can advance one schedule at the same instant, and that is safe rather than guarded: both compose the
/// same idempotency key for the same occasion, so the queue answers the second with the job the first wrote. The write
/// here is therefore last-one-wins over a value both writers agree on.
/// </para>
/// </remarks>
public interface IJobScheduleStore
{
    /// <summary>Reads what these schedules have already done.</summary>
    /// <param name="ids">The schedules to read, which is every one the configuration declares.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The state of each schedule that has one, keyed by identity; a schedule never dispatched is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ids" /> is <see langword="null" />.</exception>
    Task<IReadOnlyDictionary<string, JobScheduleState>> ReadAsync(
        IReadOnlyCollection<JobScheduleId> ids,
        CancellationToken cancellationToken);

    /// <summary>Writes what a schedule has now done, inserting the row when the schedule had none.</summary>
    /// <param name="state">The schedule as it stands after the pass decided about it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the state is durable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state" /> is <see langword="null" />.</exception>
    Task SaveAsync(JobScheduleState state, CancellationToken cancellationToken);
}
