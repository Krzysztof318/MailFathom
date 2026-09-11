// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Coordination;
using MailFathom.Application.Emails.AttachmentText.Administration;
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
/// Two of its members answer for the replica that composed it rather than for the deployment, and
/// <see cref="Replica" /> is what makes that readable: what the last provider call established is what
/// <em>this</em> process last saw, and when the next backfill pass is due is when <em>this</em> process will next
/// try for the walk's lease. Everything else is a durable figure every replica answers alike. An operator asking twice
/// and getting two different instants is reading two replicas, and without the identity beside them there is nothing
/// in the answer that says so.
/// </para>
/// <para>
/// Nothing here is mail. Model names, counts, timestamps, a health state, and a replica identity are what it holds,
/// which is what makes it safe to serve from the administrative endpoint.
/// </para>
/// </remarks>
/// <param name="Replica">The replica that composed this answer, which the two members only one process can answer for are read against.</param>
/// <param name="Declared">The geometry configuration declares, or <see langword="null" /> on an instance that declared no provider.</param>
/// <param name="Serving">The generation searches are answered from, or <see langword="null" /> when this instance has activated none.</param>
/// <param name="Building">The generation a reindex is filling, or <see langword="null" /> when no reindex is running.</param>
/// <param name="ProviderHealth">What the last call to the embedding provider established about it, as the replica that answered saw it.</param>
/// <param name="Period">Where the deployment's budget period stands.</param>
/// <param name="NextBackfillPassDueAt">
/// When the replica that answered will next take a pass, or <see langword="null" /> while it has none scheduled. An
/// instant already past is a pass that is running or about to be taken; the absence is a process that has only just
/// started, or one whose walk is turned off and will schedule nothing at all. Every replica schedules its own passes
/// and one lease decides which of them the pass belongs to, so this is when this process will next ask rather than when
/// the deployment will next walk.
/// </param>
/// <param name="AttachmentDerivation">
/// How far reading this deployment's attachments and images has come, and what its own two ceilings have consumed. A
/// member of its own rather than figures folded into the ones above, because attachment work is counted in octets and
/// in calls while embedding is counted in characters, and one mailbox may be entirely embedded on its message text
/// with every document in it still unread.
/// </param>
public sealed record EmbeddingStatus(
    ReplicaIdentity Replica,
    EmbeddingProfileIdentity? Declared,
    EmbeddingGenerationProgress? Serving,
    EmbeddingGenerationProgress? Building,
    AiProviderHealth ProviderHealth,
    EmbeddingSpendPeriod Period,
    DateTimeOffset? NextBackfillPassDueAt,
    AttachmentDerivationStatus AttachmentDerivation)
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
