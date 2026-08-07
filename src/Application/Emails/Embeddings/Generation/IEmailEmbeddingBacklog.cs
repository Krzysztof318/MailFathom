// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Generation;

/// <summary>Carries messages from the run that stored them to the worker that embeds them.</summary>
/// <remarks>
/// <para>
/// The backlog is what keeps generation off the synchronization path. A message is offered only once it and its passages
/// are durable, so the worker consumes committed state, no provider call extends an IMAP transaction, and a provider
/// outage cannot stall a fetch. Nothing an MCP request waits on reads this.
/// </para>
/// <para>
/// It is bounded, and the bound is expressed rather than absorbed: an initial synchronization of a large mailbox
/// produces work faster than any provider will accept it, so a full backlog refuses the offer instead of making the
/// producer wait. What it refuses is not lost — the message is stored with its passages, and the backfill is what
/// reaches mail the live path did not.
/// </para>
/// <para>
/// Offering one message twice is safe by construction: the work is "give every passage of this message a vector under
/// the active profile", and a second turn at a message already current finds nothing to do.
/// </para>
/// </remarks>
public interface IEmailEmbeddingBacklog
{
    /// <summary>Gets how many messages are waiting to be embedded.</summary>
    /// <remarks>Published so the depth is observable, which is what makes falling behind visible rather than invisible.</remarks>
    int Depth { get; }

    /// <summary>Offers one durable message to the worker, refusing the offer when the backlog is full.</summary>
    /// <param name="storedEmailId">The message whose passages are ready to be embedded.</param>
    /// <returns><see langword="true" /> when the message was taken; <see langword="false" /> when the bound refused it.</returns>
    /// <remarks>
    /// Never blocks and never waits. A caller that has just committed a message is holding no transaction open, but it is
    /// holding an IMAP session, and a backlog that made it wait would turn a slow provider into a slow mailbox.
    /// </remarks>
    bool TryEnqueue(StoredEmailId storedEmailId);

    /// <summary>Reads waiting messages as they arrive, until the backlog is completed or the caller cancels.</summary>
    /// <param name="cancellationToken">Ends the sequence when the host stops.</param>
    /// <returns>The waiting messages, in the order they were offered.</returns>
    /// <remarks>One consumer at a time. A message read from here has been removed from the backlog whether or not embedding it succeeds.</remarks>
    IAsyncEnumerable<StoredEmailId> ReadAllAsync(CancellationToken cancellationToken);
}
