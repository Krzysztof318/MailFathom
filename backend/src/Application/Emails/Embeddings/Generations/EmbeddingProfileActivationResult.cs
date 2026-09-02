// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>What one activation did, and which generation it did it to.</summary>
/// <remarks>
/// The identifier is present on every outcome, including the refused one, because what an operator needs next differs
/// by outcome and each answer names a row: the generation now being built, the one already serving, or the one whose
/// reindex is in the way.
/// </remarks>
/// <param name="Outcome">What the activation did.</param>
/// <param name="ProfileId">The generation the outcome is about.</param>
public sealed record EmbeddingProfileActivationResult(
    EmbeddingProfileActivationOutcome Outcome,
    EmbeddingProfileId ProfileId);
