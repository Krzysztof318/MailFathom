// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at the part of the bucket a sweep has not reached yet, and at nothing inside a message.</summary>
/// <remarks>
/// <para>
/// A sweep of a whole bucket is more work than one attempt may hold, so it is carried by a chain of jobs: the schedule
/// dispatches the first naming the sweep it begins, and every one that stops with objects still ahead of it hands the
/// rest to the next. The four properties are what the next one needs to be both resumable and enqueueable.
/// </para>
/// <para>
/// <see cref="SweepId" /> and <see cref="Segment" /> exist for the queue rather than for the work. An idempotency key
/// is unique for the life of the table, so a chain that reused one would have its second segment silently answered as
/// a job already enqueued; the sweep the chain belongs to and the place in it are what make each key its own.
/// </para>
/// <para>
/// <b>The sweep is named by the occasion that dispatched it rather than by the attempt that carries it.</b> The
/// identity travels in the document the enqueue committed, so every execution of one leased segment reads the same one
/// and composes the same hand-on key. Minting it inside <see cref="ContinuingFrom" /> would make it a property of the
/// attempt instead: an attempt whose work succeeded and whose completion the store then refused is repeated against the
/// same payload, and two mints would hand the same position to two keys and fork one sweep into two chains walking one
/// bucket.
/// </para>
/// <para>
/// <see cref="ResumeFrom" /> is a position in the endpoint's listing. It is the one value here that came from the store
/// rather than from MailFathom, and it names a place among keys rather than anything read out of a message — which is
/// the same standard every other payload in this queue is written to.
/// </para>
/// </remarks>
public sealed record ReclaimContentObjectsJobPayload : IJobPayload
{
    /// <summary>Gets the sweep this job is a segment of, which the occasion that dispatched the chain named.</summary>
    /// <remarks>
    /// Required, so a document carrying none is refused by the deserializer rather than resolving to a sweep nobody
    /// named. A first segment a previous release enqueued and had not yet run is that case, and it is refused for the
    /// reason every payload record refuses a component that no longer validates: the occasion after it dispatches a
    /// sweep of its own, so what the refusal costs is one interval rather than any object left unswept.
    /// </remarks>
    public required string SweepId { get; init; }

    /// <summary>Gets which segment of that sweep this job carries, counted from the one the schedule dispatched.</summary>
    public int Segment { get; init; }

    /// <summary>Gets the position in the listing this segment begins at, or <see langword="null" /> to begin at its start.</summary>
    public string? ResumeFrom { get; init; }

    /// <summary>Gets the age of the oldest orphan the segments before this one met, zero for the segment that begins a sweep.</summary>
    /// <remarks>
    /// Carried for the same reason <see cref="ResumeFrom" /> is. The gauge that says how far behind reclamation has
    /// fallen is written by the segment that reaches the end of the listing, and it can only speak for the whole bucket
    /// if what the earlier segments met travels with the position they stopped at. A duration is not a reference to
    /// anything in a message, so it costs the payload's privacy rule nothing.
    /// </remarks>
    public TimeSpan OldestOrphanAge { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.ReclaimContentObjects;

    /// <summary>Describes the first segment of one sweep, which begins at the start of the listing.</summary>
    /// <param name="sweepId">The identity of the sweep this occasion begins, which every segment of it is keyed by.</param>
    /// <returns>The payload a schedule dispatches.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sweepId" /> is blank, because a segment belonging to no sweep composes no key.</exception>
    public static ReclaimContentObjectsJobPayload FromTheStart(string sweepId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sweepId);

        return new ReclaimContentObjectsJobPayload { SweepId = sweepId };
    }

    /// <summary>Describes the segment that carries whatever this one did not reach.</summary>
    /// <param name="resumeFrom">The position the run stopped at.</param>
    /// <param name="oldestOrphanAge">The oldest orphan the sweep has met up to that position.</param>
    /// <returns>The payload of the next segment, belonging to the same sweep as this one.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resumeFrom" /> is blank, because a segment that resumes nowhere is the first one.</exception>
    public ReclaimContentObjectsJobPayload ContinuingFrom(string resumeFrom, TimeSpan oldestOrphanAge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeFrom);

        return this with
        {
            Segment = this.Segment + 1,
            ResumeFrom = resumeFrom,
            OldestOrphanAge = oldestOrphanAge,
        };
    }

    /// <summary>Composes the identity under which this segment is enqueued.</summary>
    /// <returns>The idempotency key, which no other segment of any sweep shares.</returns>
    /// <remarks>
    /// The first segment is enqueued by the schedule under the occasion's own key rather than under this one, so the
    /// key a first segment composes is only ever the one its successor is handed on under.
    /// </remarks>
    public JobIdempotencyKey ToIdempotencyKey() =>
        JobIdempotencyKey.Create($"{JobType.ReclaimContentObjects.Name}:{this.SweepId}:{this.Segment}");
}
