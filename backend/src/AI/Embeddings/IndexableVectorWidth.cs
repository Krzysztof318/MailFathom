// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Embeddings;

/// <summary>The widths pgvector can store and index, which is what makes model width a database decision.</summary>
/// <remarks>
/// pgvector stores far more than it indexes. MailFathom builds no vector index — every profile is searched exactly,
/// measured rather than assumed, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/architecture/semantic-ranking-cost.md">What a
/// semantic search costs</see> holds what decided that — so what a width above <see cref="GreatestIndexable" /> costs
/// is storage and scan time rather than a search that never becomes fast.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// keeps the ceiling an explicit declaration regardless: a declared width above it is refused unless the deployment
/// says it accepts a narrowed one.
/// </remarks>
public static class IndexableVectorWidth
{
    /// <summary>The greatest number of dimensions a <c>vector</c> column stores.</summary>
    public const int GreatestStorable = 16000;

    /// <summary>The greatest number of dimensions an HNSW index covers, which is what bounds a declared width.</summary>
    /// <remarks>
    /// Half precision would raise this to 4000 and is deliberately not adopted: it would be a second column, a second
    /// index path, and a second set of distance operators for the sake of one model width. ADR 0006 records it as the
    /// first thing to revisit if a model between this ceiling and 4000 becomes the one the project wants.
    /// </remarks>
    public const int GreatestIndexable = 2000;
}
