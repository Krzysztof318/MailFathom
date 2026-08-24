// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at the part of the bucket a sweep has not reached yet, and at nothing inside a message.</summary>
/// <remarks>
/// <para>
/// A sweep of a whole bucket is more work than one attempt may hold, so it is carried by a chain of jobs: the schedule
/// dispatches the first with nothing filled in, and every one that stops with objects still ahead of it hands the rest
/// to the next. The three properties are what the next one needs to be both resumable and enqueueable.
/// </para>
/// <para>
/// <see cref="SweepId" /> and <see cref="Segment" /> exist for the queue rather than for the work. An idempotency key
/// is unique for the life of the table, so a chain that reused one would have its second segment silently answered as
/// a job already enqueued; the sweep the chain belongs to and the place in it are what make each key its own.
/// </para>
/// <para>
/// <see cref="ResumeFrom" /> is a position in the endpoint's listing. It is the one value here that came from the store
/// rather than from MailFathom, and it names a place among keys rather than anything read out of a message — which is
/// the same standard every other payload in this queue is written to.
/// </para>
/// </remarks>
public sealed record ReclaimContentObjectsJobPayload : IJobPayload
{
    /// <summary>Gets the sweep this job is a segment of, or <see langword="null" /> for the first segment a schedule dispatched.</summary>
    public string? SweepId { get; init; }

    /// <summary>Gets which segment of that sweep this job carries, counted from the first hand-on.</summary>
    public int Segment { get; init; }

    /// <summary>Gets the position in the listing this segment begins at, or <see langword="null" /> to begin at its start.</summary>
    public string? ResumeFrom { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.ReclaimContentObjects;

    /// <summary>Describes the first segment of a sweep, which begins at the start of the listing.</summary>
    /// <returns>The payload a schedule dispatches.</returns>
    public static ReclaimContentObjectsJobPayload FromTheStart() => new();

    /// <summary>Describes the segment that carries whatever this one did not reach.</summary>
    /// <param name="resumeFrom">The position the run stopped at.</param>
    /// <returns>The payload of the next segment, belonging to the same sweep as this one.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resumeFrom" /> is blank, because a segment that resumes nowhere is the first one.</exception>
    /// <remarks>
    /// The sweep identity is minted here when the chain began at a schedule, so a segment always belongs to a named
    /// sweep however it was reached. It is a version 7 identifier because a chain is read in the order it ran.
    /// </remarks>
    public ReclaimContentObjectsJobPayload ContinuingFrom(string resumeFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeFrom);

        return new ReclaimContentObjectsJobPayload
        {
            SweepId = this.SweepId ?? Guid.CreateVersion7().ToString(),
            Segment = this.Segment + 1,
            ResumeFrom = resumeFrom,
        };
    }

    /// <summary>Composes the identity under which this segment is enqueued.</summary>
    /// <returns>The idempotency key, which no other segment of any sweep shares.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the payload names no sweep, which is the first segment and is enqueued by the schedule under the occasion's own key.</exception>
    public JobIdempotencyKey ToIdempotencyKey() => this.SweepId is { Length: > 0 } sweepId
        ? JobIdempotencyKey.Create($"{JobType.ReclaimContentObjects.Name}:{sweepId}:{this.Segment}")
        : throw new InvalidOperationException(
            "The first segment of a sweep is enqueued by the schedule under the occasion it was dispatched on, so it composes no key of its own.");
}
