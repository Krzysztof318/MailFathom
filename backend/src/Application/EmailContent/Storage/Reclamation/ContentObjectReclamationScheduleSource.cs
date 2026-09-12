// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    private readonly JobRecurrence recurrence;

    /// <summary>Initializes the source over the interval the deployment configured.</summary>
    /// <param name="recurrence">The occasions a sweep is dispatched on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recurrence" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The interval is held rather than read per pass, unlike the sources over rules and over a user's declarations.
    /// Those change while the process runs; this one is a setting the host reads once, because a bucket cannot be
    /// repointed without the client being rebuilt in any case.
    /// </remarks>
    public ContentObjectReclamationScheduleSource(JobRecurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);

        this.recurrence = recurrence;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The declaration is composed per read because the sweep it dispatches is named here, which is the only place that
    /// can name it before an attempt exists. The name reaches the chain in the document the enqueue committed, so every
    /// execution of the first leased segment reads one identity and hands the rest of the sweep on under one key.
    /// </para>
    /// <para>
    /// Which of the names a pass proposes becomes the occasion's is decided by the queue rather than here. Every pass
    /// on every replica enqueues one occasion under one key, so the first to arrive writes the sweep and the rest are
    /// answered with the job already there — and two occasions are two keys, two documents, and two chains that are
    /// never deduped against each other.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduledJob> declared =
        [
            new ScheduledJob(
                JobScheduleId.Create(ScheduleIdentity),
                ReclaimContentObjectsJobPayload.FromTheStart(Guid.CreateVersion7().ToString()),
                this.recurrence),
        ];

        return Task.FromResult(declared);
    }
}
