// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;

namespace MailFathom.Host.Api;

/// <summary>The move of stored content into the object backend, as the administrative endpoint serves it.</summary>
/// <param name="State">What the move is doing, as one of <c>running</c>, <c>paused</c>, or <c>completed</c>.</param>
/// <param name="RequestedAt">When it was asked for.</param>
/// <param name="CopiedPayloadCount">How many payloads it has copied, verified, and repointed.</param>
/// <param name="FailedPayloadCount">How many payloads it left in the database because a copy could not be verified.</param>
/// <param name="MovedByteCount">How many bytes of raw MIME the copied payloads carried.</param>
/// <param name="EndedAt">When it reached the end of the content, or nothing while it has not.</param>
/// <remarks>
/// Counts, instants, and one state. The position the next pass resumes from is deliberately not served: it is an
/// identity of a row rather than a figure an operator acts on, and it says which message the move is currently at.
/// </remarks>
internal sealed record ContentMoveRunResponse(
    string State,
    DateTimeOffset RequestedAt,
    long CopiedPayloadCount,
    long FailedPayloadCount,
    long MovedByteCount,
    DateTimeOffset? EndedAt)
{
    /// <summary>Describes one move for the wire.</summary>
    /// <param name="run">The move as the deployment holds it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    internal static ContentMoveRunResponse For(StoredContentMoveRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ContentMoveRunResponse(
            NameOf(run.State),
            run.RequestedAt,
            run.CopiedPayloadCount,
            run.FailedPayloadCount,
            run.MovedByteCount,
            run.EndedAt);
    }

    /// <summary>Names one state on the wire, so a client reads a value rather than an ordinal.</summary>
    /// <remarks>
    /// Written out here rather than serialized from the enum, because the enum's members are the application's to rename
    /// and these names are a published contract; keeping the two apart is what lets one move without the other.
    /// </remarks>
    private static string NameOf(StoredContentMoveState state) => state switch
    {
        StoredContentMoveState.Running => "running",
        StoredContentMoveState.Paused => "paused",
        StoredContentMoveState.Completed => "completed",
        _ => "unknown",
    };
}

/// <summary>Where the move has got to, and how much of the deployment's content the database still holds.</summary>
/// <param name="Available">Whether this deployment has an object backend its content could be moved into.</param>
/// <param name="Run">The move it last had, or nothing when none was ever asked for.</param>
/// <param name="RemainingPayloadCount">How many payloads of any kind the database still holds.</param>
/// <param name="RemainingByteCount">How many bytes of raw MIME they carry between them.</param>
/// <remarks>
/// The backlog is served whether or not a move exists, because it is the figure an operator weighs before asking for
/// one — and a deployment storing content in the database answers it too, which is what makes this route the honest
/// place to ask what a switch would cost.
/// </remarks>
internal sealed record ContentMoveStateResponse(
    bool Available,
    ContentMoveRunResponse? Run,
    long RemainingPayloadCount,
    long RemainingByteCount)
{
    /// <summary>Describes what the deployment answered about its move.</summary>
    /// <param name="available">Whether an object backend is configured at all.</param>
    /// <param name="progress">The move and the backlog, read as one.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="progress" /> is <see langword="null" />.</exception>
    internal static ContentMoveStateResponse For(bool available, StoredContentMoveProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return new ContentMoveStateResponse(
            available,
            progress.Run is null ? null : ContentMoveRunResponse.For(progress.Run),
            progress.Backlog.PayloadCount,
            progress.Backlog.ByteCount);
    }
}
