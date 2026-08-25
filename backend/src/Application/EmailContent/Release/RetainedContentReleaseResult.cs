// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;

namespace MailFathom.Application.EmailContent.Release;

/// <summary>What one release freed, what is still retained behind it, and what stopped it where it was stopped.</summary>
/// <remarks>
/// The three figures are read together because none of them answers the operator's question alone: what a batch freed
/// says nothing about whether to ask again, what is retained says nothing about whether asking is permitted, and the
/// backlog the move left is the one reason a release performs nothing at all.
/// </remarks>
/// <param name="Released">What this request freed, which is nothing when it was refused.</param>
/// <param name="Retained">What the database still holds beside an object, after this request.</param>
/// <param name="AwaitingMove">What the database still owns and the move has not carried, which is what a refusal names.</param>
public sealed record RetainedContentReleaseResult(
    ReleasedContentPayloads Released,
    StoredContentBacklog Retained,
    StoredContentBacklog AwaitingMove)
{
    /// <summary>Gets whether the release was refused because content is still waiting to be carried into the bucket.</summary>
    /// <remarks>
    /// A payload the database still owns is one no object was ever verified for, so releasing anything while one exists
    /// would end the safety of a move whose own work is unfinished. The refusal names the backlog rather than the
    /// payloads, because what repairs it is another move rather than a decision about any one message.
    /// </remarks>
    public bool WasRefused => this.AwaitingMove.PayloadCount > 0;

    /// <summary>Gets whether any retained copy is left for a further release to reach.</summary>
    public bool PayloadsRemain => this.Retained.PayloadCount > 0;
}
