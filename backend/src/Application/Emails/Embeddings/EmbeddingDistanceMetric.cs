// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Names how the distance between two vectors of one space is measured.</summary>
/// <remarks>
/// Part of an embedding profile's identity rather than a property of a query, because two vectors are only comparable
/// under the metric the model was trained for: reading a space built for cosine distance under an inner product returns
/// a number rather than an error, and the wrong number looks exactly like the right one.
/// </remarks>
public enum EmbeddingDistanceMetric
{
    /// <summary>The angle between two vectors, which is what a normalized embedding model is trained for.</summary>
    Cosine = 0,

    /// <summary>The negative inner product, for a model whose magnitude carries meaning.</summary>
    InnerProduct = 1,

    /// <summary>The straight-line distance between two points of the space.</summary>
    EuclideanDistance = 2,
}
