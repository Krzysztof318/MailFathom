// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;

namespace MailFathom.Application.EmailContent.Storage.Reclamation;

/// <summary>Declares the one recurring sweep a deployment storing mail in a bucket runs.</summary>
/// <remarks>
/// <para>
/// One schedule for the whole deployment rather than one per account, because what it sweeps is one bucket under one
/// prefix and an object gives no account away — a key is minted by the write that produced it and names nothing about
/// the message it holds. Splitting it per account would mean listing the same bucket once per mailbox to find the
/// objects of one.
/// </para>
/// <para>
/// It is registered only where the deployment named an endpoint, so a deployment storing content in the database
/// declares no schedule and dispatches nothing. The interval it declares is a privacy-relevant setting rather than
/// housekeeping: it is the bound on how long mail whose record is already gone can still exist as bytes.
/// </para>
/// </remarks>
public sealed class ContentObjectReclamationScheduleSource : IScheduledJobSource
{
    /// <summary>The identity the sweep's durable state is keyed by, which is one for the whole deployment.</summary>
    internal const string ScheduleIdentity = "content-object-reclamation";

    private readonly IReadOnlyList<ScheduledJob> declared;

    /// <summary>Initializes the source over the interval the deployment configured.</summary>
    /// <param name="recurrence">The occasions a sweep is dispatched on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recurrence" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Held rather than read per pass, unlike the sources over rules and over an owner's declarations. Those change
    /// while the process runs; this one is composed from a setting the host reads once, because a bucket cannot be
    /// repointed without the client being rebuilt in any case.
    /// </remarks>
    public ContentObjectReclamationScheduleSource(JobRecurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);

        this.declared =
        [
            new ScheduledJob(
                JobScheduleId.Create(ScheduleIdentity),
                ReclaimContentObjectsJobPayload.FromTheStart(),
                recurrence),
        ];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(this.declared);
}
