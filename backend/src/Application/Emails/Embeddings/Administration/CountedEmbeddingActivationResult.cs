// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Generations;

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>What one counted activation did, beside the counting it did first.</summary>
/// <remarks>
/// The assessment travels with the result rather than being discarded once the decision was taken, because both endings
/// need it: a refusal has to name the estimate and the ceiling it was refused against, and a run that started has to
/// report what it started against so the operator can recognize the figure they confirmed.
/// </remarks>
/// <param name="Assessment">What the activation was weighed as, taken immediately before it ran.</param>
/// <param name="Activation">What the activation did, or <see langword="null" /> when the spend ceiling refused it.</param>
public sealed record CountedEmbeddingActivationResult(
    EmbeddingActivationAssessment Assessment,
    EmbeddingProfileActivationResult? Activation)
{
    /// <summary>Gets whether the deployment's spend ceiling refused this activation before anything was written.</summary>
    /// <remarks>
    /// The absence of an activation is the refusal rather than a state beside it: this operation either registered a
    /// generation, reported one that was already there, or was refused, and there is no fourth ending in which nothing
    /// happened for another reason.
    /// </remarks>
    public bool RefusedBySpendCeiling => this.Activation is null;
}
