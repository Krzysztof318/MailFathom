// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Cuts one message into the passages retrieval is built from, and removes them again.</summary>
/// <remarks>
/// <para>
/// The two halves are one port because they are the two ends of one decision. Cutting is the first thing that happens
/// to a message downstream of classification, and it is deliberately a call the synchronization run makes rather than
/// something the metadata write does on its way past: what decides whether a message is cut is what classification says
/// about it, and a write that cut every message it stored would have decided before anything could score it.
/// </para>
/// <para>
/// Removing is the other end of the same decision, for the one case the ordering cannot reach — a message chunked and
/// embedded before anybody scored it, which an on-demand run over an existing mailbox produces. What it leaves behind is
/// vectors nothing may retrieve, derived from the most adversarial text in the mailbox and kept under the message's own
/// retention obligations for no reader.
/// </para>
/// <para>
/// Neither half touches the message, its content, or its search document. Passages are derived data and this port says
/// nothing about whether the mailbox should still hold what they were derived from.
/// </para>
/// </remarks>
public interface IEmailChunkStore
{
    /// <summary>Stages the passages one extraction yields for a message, leaving an unchanged message's passages alone.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="emailId">The message the passages belong to, which the session may still be holding uncommitted.</param>
    /// <param name="text">The text an extraction derived from the message's body.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the passages are staged, or immediately when nothing changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Idempotent by the text rather than by a flag: a message whose text and rules have not moved yields the identical
    /// passages and this writes nothing, which is what makes it safe for the arrival path, a repair, and a backfill to
    /// all ask. A folder configured not to embed is cut into no passages whichever of them asks.
    /// </remarks>
    Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        ExtractedEmailText text,
        CancellationToken cancellationToken);

    /// <summary>Stages the removal of every passage cut from one message.</summary>
    /// <param name="session">The explicit persistence session this removal participates in.</param>
    /// <param name="emailId">The message whose passages are removed.</param>
    /// <param name="cancellationToken">Cancels the read before anything is staged.</param>
    /// <returns>How many passages the removal reached, which is zero for a message that was never chunked.</returns>
    /// <remarks>
    /// Idempotent, so a caller that repeats it removes nothing the second time and reports zero. Staged rather than
    /// committed, so the removal and whatever decided on it reach the database together.
    /// </remarks>
    Task<int> DiscardChunksAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        CancellationToken cancellationToken);
}
