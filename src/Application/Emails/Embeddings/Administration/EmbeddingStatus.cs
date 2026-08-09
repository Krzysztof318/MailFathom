// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings.Limits;

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>Whether semantic search is working on this instance, and where it is if it is not yet.</summary>
/// <remarks>
/// <para>
/// One value rather than five readings an operator has to combine, because the question it answers is one question.
/// Semantic search can be absent for reasons that look nothing alike — no provider declared, a declaration nobody
/// activated, a provider refusing the credential, a reindex still running, a budget period spent, a pass that is
/// simply not due yet — and each of them is answered by a different member here. Reading them apart would leave an
/// operator checking five things to learn that the sixth was the problem.
/// </para>
/// <para>
/// Nothing here is mail. Model names, counts, timestamps, and a health state are what it holds, which is what makes it
/// safe to serve from the administrative endpoint.
/// </para>
/// </remarks>
/// <param name="Declared">The geometry configuration declares, or <see langword="null" /> on an instance that declared no provider.</param>
/// <param name="Serving">The generation searches are answered from, or <see langword="null" /> when this instance has activated none.</param>
/// <param name="Building">The generation a reindex is filling, or <see langword="null" /> when no reindex is running.</param>
/// <param name="ProviderHealth">What the last call to the embedding provider established about it.</param>
/// <param name="Period">Where the deployment's budget period stands.</param>
/// <param name="NextBackfillPassDueAt">
/// When the backfill's next pass is due, or <see langword="null" /> while none is scheduled. An instant already past is
/// a pass that is running or about to be taken; the absence is an instance that has only just started, or one whose
/// walk is turned off and will schedule nothing at all.
/// </param>
public sealed record EmbeddingStatus(
    EmbeddingProfileIdentity? Declared,
    EmbeddingGenerationProgress? Serving,
    EmbeddingGenerationProgress? Building,
    AiProviderHealth ProviderHealth,
    EmbeddingSpendPeriod Period,
    DateTimeOffset? NextBackfillPassDueAt)
{
    /// <summary>Gets whether a declaration is waiting for an activation nobody has performed.</summary>
    /// <remarks>
    /// <para>
    /// The member this whole value exists for. Editing configuration declares a model and starts nothing, which
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
    /// records as the cost of keeping the declaration reviewable — so an operator who edited a file and expected search
    /// results to change learns here that they have not, rather than from search results that stayed the same.
    /// </para>
    /// <para>
    /// Computed from the fingerprints rather than carried as a flag a caller sets, so it cannot disagree with the
    /// generations beside it. A generation being built counts as having taken the declaration up: the activation
    /// happened and the run is under way.
    /// </para>
    /// </remarks>
    public bool ActivationOutstanding
    {
        get
        {
            if (this.Declared is not { } declared)
            {
                return false;
            }

            var declaredFingerprint = EmbeddingProfileFingerprint.Compute(declared);

            return !TakesUp(this.Serving, declaredFingerprint) && !TakesUp(this.Building, declaredFingerprint);
        }
    }

    private static bool TakesUp(EmbeddingGenerationProgress? generation, EmbeddingProfileFingerprint declared) =>
        generation is { } present && EmbeddingProfileFingerprint.Compute(present.Profile.Identity) == declared;
}
