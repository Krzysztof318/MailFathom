// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Vectorization;

/// <summary>How one message's turn at being embedded ended.</summary>
/// <remarks>
/// Three of these are conditions of the instance rather than of the message, and they are apart because they ask an
/// operator for different things: activate a profile, reconcile a declaration with what was activated, or wait for a
/// provider. The other two are about the mail, and they are apart for the same reason: a message that is now whole and
/// one that was left part-way through are not the same answer.
/// </remarks>
public enum StoredEmailEmbeddingOutcome
{
    /// <summary>Every passage of the message now carries a vector under the active profile.</summary>
    /// <remarks>Reached with a count of zero by a message that was already current, which is the ordinary result of offering one twice.</remarks>
    Embedded = 0,

    /// <summary>This instance has activated no profile, so there is no space to place a passage in.</summary>
    /// <remarks>Not a failure. An instance serving lexical search alone is a supported deployment, and this is what it looks like from here.</remarks>
    NoActiveProfile = 1,

    /// <summary>The generator this process is configured with produces vectors of a different space than the active profile records.</summary>
    /// <remarks>
    /// Terminal until an operator acts, and refused rather than written: vectors of another geometry stored under this
    /// profile would make retrieval quietly worse instead of failing, which is the hardest kind of defect to attribute.
    /// It is what an edited declaration nobody activated looks like from the generation path.
    /// </remarks>
    GeneratorDisagreesWithProfile = 2,

    /// <summary>A provider call ended without vectors, and <see cref="StoredEmailEmbeddingRun.Failure" /> says how.</summary>
    /// <remarks>Whatever was committed before the failure stays durable; the passages the call was for keep waiting.</remarks>
    ProviderFailed = 3,

    /// <summary>One message's turn spent every provider call it is allowed and passages of it are still outstanding.</summary>
    /// <remarks>
    /// Distinct from <see cref="Embedded" /> because the two are opposite statements about the same message, and
    /// collapsing them would report a truncated message as a complete one — the kind of defect nothing later can
    /// notice, because a partly embedded message is retrievable and simply answers worse. What was committed stays
    /// durable and the rest stays outstanding, which is the condition the backfill selects on, so nothing is lost; what
    /// an operator learns from it is that one message needed more calls than a turn is allowed, which is a batch size
    /// far below what a message of that length needs.
    /// </remarks>
    CallBudgetExhausted = 4,

    /// <summary>The period's spend ceiling is reached, so nothing further is sent until it rolls over.</summary>
    /// <remarks>
    /// A condition of the instance rather than of the message, and the only one of them that resolves itself: the
    /// period rolls over at <see cref="StoredEmailEmbeddingRun.SpendPeriodEndsAt" /> and work continues, with no
    /// operator having to do anything. It is deliberately not the same outcome as
    /// <see cref="CallBudgetExhausted" /> beside it, which bounds how many calls one message's turn may make and says
    /// something about that message's length; this says the deployment has spent what it agreed to spend, and says
    /// nothing about the message at all. What was committed stays durable and the rest stays outstanding, which is the
    /// condition the backfill selects on.
    /// </remarks>
    SpendCeilingReached = 5,
}
