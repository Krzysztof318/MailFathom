// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generation;

/// <summary>Bounds how much embedding work one process holds in memory before it starts refusing more.</summary>
/// <remarks>
/// The bound is on messages rather than on passages, because a message is what a producer offers and what a consumer
/// takes; how many passages one message holds is decided by the chunking rules and by the message itself. Operational
/// rather than part of a profile's identity, so changing it re-embeds nothing.
/// </remarks>
public sealed record EmailEmbeddingBacklogOptions
{
    /// <summary>The bound applied when a deployment configures none.</summary>
    /// <remarks>
    /// Large enough that an ordinary arrival of mail never reaches it, and small enough that the first synchronization
    /// of a large mailbox reaches it quickly — which is the intended outcome rather than a defect, because the backlog
    /// exists to hold work in flight rather than to hold a mailbox.
    /// </remarks>
    public const int DefaultCapacity = 1024;

    /// <summary>Gets the greatest number of messages that may wait to be embedded at once.</summary>
    public int Capacity { get; init; } = DefaultCapacity;
}
