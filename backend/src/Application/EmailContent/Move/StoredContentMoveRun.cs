// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>The move of a deployment's stored content into the bucket, and how far the passes carrying it have come.</summary>
/// <remarks>
/// <para>
/// Durable because it has to survive the process. A mailbox of any size outlives the host that started copying it, so a
/// restart resumes where the last pass committed rather than at the first payload — and an operator who paused the move
/// before a rolling restart must find it paused afterwards rather than running again.
/// </para>
/// <para>
/// One move per deployment, which the store makes structural rather than checking. The backend is one setting for the
/// whole instance under
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1,
/// so there is no second scope a second move could cover, and two operators asking together are asking for one thing.
/// </para>
/// <para>
/// The position is a payload kind and the identity the last pass reached inside it, walked in the order the kinds are
/// declared. A payload that moved leaves the walk's own set, so the position is what carries the walk past the payloads
/// it could not move rather than what makes it terminate.
/// </para>
/// <para>
/// Every field here is a count, an instant, a state, or MailFathom's own identity for a payload. Nothing derived from a
/// message belongs in a record an operator reads to find out what their instance is doing.
/// </para>
/// </remarks>
public sealed record StoredContentMoveRun
{
    /// <summary>Gets when the move was asked for.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets what the move is doing, which only an operator and the pass that finishes the walk change.</summary>
    public required StoredContentMoveState State { get; init; }

    /// <summary>Gets the payload kind the walk is currently inside.</summary>
    public required EmailContentKind Kind { get; init; }

    /// <summary>Gets the payload the last pass reached inside that kind, or <see langword="null" /> at the start of one.</summary>
    /// <remarks>
    /// The identity of the owning row rather than an object key, because the walk is ordered by that identity: it is
    /// total, stable, and already the primary key of every one of the four content tables.
    /// </remarks>
    public Guid? ResumeAfter { get; init; }

    /// <summary>Gets how many payloads the move copied, verified, and repointed at the bucket.</summary>
    public long CopiedPayloadCount { get; init; }

    /// <summary>Gets how many payloads the move refused to repoint, each of which is still held in the database.</summary>
    public long FailedPayloadCount { get; init; }

    /// <summary>Gets how many bytes of raw MIME the copied payloads carried.</summary>
    public long MovedByteCount { get; init; }

    /// <summary>Gets when the walk reached the end of the last payload kind, or <see langword="null" /> while it has not.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets whether the move still has payloads to reach.</summary>
    public bool IsOutstanding => this.State is not StoredContentMoveState.Completed;
}
