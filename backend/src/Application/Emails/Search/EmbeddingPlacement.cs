// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.Application.Emails.Search;

/// <summary>What placing text in the active vector space produced: the capability it ran under, and the vectors when there are any.</summary>
/// <param name="Capability">What semantic retrieval could do for this call, which a search reports beside its answer.</param>
/// <param name="Profile">The space the vectors belong to, or <see langword="null" /> where nothing was placed.</param>
/// <param name="Vectors">One vector per text in the order they were given, or <see langword="null" /> where nothing was placed.</param>
/// <remarks>The profile and the vectors are present together or absent together, because a vector is meaningful only beside the space it was placed in.</remarks>
public sealed record EmbeddingPlacement(
    SemanticSearchCapability Capability,
    RegisteredEmbeddingProfile? Profile,
    IReadOnlyList<EmbeddingVector>? Vectors);
