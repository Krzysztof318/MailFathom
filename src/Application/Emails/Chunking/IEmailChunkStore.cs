// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Takes away the passages retrieval is built from, for a message that turned out not to deserve them.</summary>
/// <remarks>
/// <para>
/// Removal is the one end of the cut that ordering cannot supply. Everything else about a passage is decided before it
/// exists — <see cref="IStoredEmailChunkingStore" /> cuts a message only once classification and the owner's rules have
/// finished with it — but a message chunked and embedded before anybody scored it is what an on-demand classification
/// run over an existing mailbox produces, and what it leaves behind is vectors nothing may retrieve, derived from the
/// most adversarial text in the mailbox and kept under the message's own retention obligations for no reader.
/// </para>
/// <para>
/// It touches neither the message, its content, nor its search document. Passages are derived data and this port says
/// nothing about whether the mailbox should still hold what they were derived from.
/// </para>
/// </remarks>
public interface IEmailChunkStore
{
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
