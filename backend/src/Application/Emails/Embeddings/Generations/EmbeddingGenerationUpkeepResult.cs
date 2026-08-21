// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>What one upkeep pass did to this instance's generations.</summary>
/// <remarks>
/// Counts and classifications only, like every other result a worker reports: the messages a pass reached, the vectors
/// it wrote, and the vectors it removed are all derived from mail, and none of them belongs in a log.
/// </remarks>
/// <param name="Sweep">What the walk towards the target generation produced, and why it ended.</param>
/// <param name="Transition">Whether the generation being built became the one retrieval reads.</param>
/// <param name="RemovedSupersededVectorCount">How many vectors of a replaced generation this pass removed.</param>
public sealed record EmbeddingGenerationUpkeepResult(
    StoredEmailEmbeddingBackfillResult Sweep,
    EmbeddingGenerationTransition Transition,
    int RemovedSupersededVectorCount)
{
    /// <summary>Gets whether running again shortly would reach work this pass could not.</summary>
    /// <remarks>
    /// Three things pace the loop rather than one. The sweep answers for the mail it is embedding; a switch means the
    /// next pass has a whole generation to clear out, and waiting an idle interval to start it would leave a mailbox's
    /// worth of superseded vectors in place for no reason; and a removal that reached its bound has more behind it,
    /// which is the same argument the sweep's own budget makes.
    /// </remarks>
    public bool MoreWorkIsWorthTryingSoon =>
        this.Sweep.MoreWorkIsWorthTryingSoon
        || this.Transition == EmbeddingGenerationTransition.Switched
        || this.RemovedSupersededVectorCount > 0;
}
