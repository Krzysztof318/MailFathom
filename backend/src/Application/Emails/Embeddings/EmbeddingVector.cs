// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>One point of an embedding profile's vector space.</summary>
/// <remarks>
/// <para>
/// A class rather than a record struct, because the components are a <see cref="ReadOnlyMemory{T}" /> whose synthesized
/// equality would compare the segment rather than the numbers in it, and two vectors that are equal component for
/// component would report otherwise.
/// </para>
/// <para>
/// The type owns the two arithmetic operations an adapter needs — shortening and normalizing — so that the rule
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// states about a shortened vector lives in one place and is provable without a provider. Nothing here decides
/// <em>whether</em> to shorten; that is the declaration's decision and the adapter applies it.
/// </para>
/// </remarks>
public sealed class EmbeddingVector
{
    private readonly float[] components;

    private EmbeddingVector(float[] components) => this.components = components;

    /// <summary>Gets the number of components, which is the width of the space this vector belongs to.</summary>
    public int Dimension => this.components.Length;

    /// <summary>Gets the components in the order the provider produced them.</summary>
    /// <remarks>
    /// A <see cref="ReadOnlyMemory{T}" /> rather than an array, so the value crosses an asynchronous boundary without
    /// handing a caller something it could write through.
    /// </remarks>
    public ReadOnlyMemory<float> Components => this.components;

    /// <summary>Builds a vector from the components a provider returned.</summary>
    /// <param name="components">The components, which are copied so a later write to the source cannot change the vector.</param>
    /// <returns>The vector.</returns>
    /// <exception cref="ArgumentException">Thrown when the sequence is empty or holds a value that is not a finite number.</exception>
    /// <remarks>
    /// A non-finite component is refused here rather than stored, because it survives every distance operator as a
    /// result that is neither an error nor a number: a `NaN` compares false against everything, so the chunk carrying
    /// it silently stops being retrievable instead of failing.
    /// </remarks>
    public static EmbeddingVector Create(ReadOnlySpan<float> components)
    {
        if (components.IsEmpty)
        {
            throw new ArgumentException("A vector has at least one component.", nameof(components));
        }

        foreach (var component in components)
        {
            if (!float.IsFinite(component))
            {
                throw new ArgumentException(
                    "Every component of a vector is a finite number.",
                    nameof(components));
            }
        }

        return new EmbeddingVector(components.ToArray());
    }

    /// <summary>Shortens the vector to a narrower space and restores its unit length.</summary>
    /// <param name="dimension">The width of the narrower space, which must not exceed this vector's own.</param>
    /// <returns>The shortened vector, or this one when it already has that width.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the width is not positive or exceeds <see cref="Dimension" />.</exception>
    /// <remarks>
    /// Renormalization is part of shortening rather than a separate step a caller might forget. Dropping the tail of a
    /// unit vector leaves one whose length is below one by however much the dropped components carried, and a cosine
    /// distance computed against vectors of differing lengths is a number with no meaning rather than an error.
    /// </remarks>
    public EmbeddingVector Shorten(int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dimension, this.Dimension);

        return dimension == this.Dimension
            ? this
            : new EmbeddingVector(this.components[..dimension]).Normalize();
    }

    /// <summary>Scales the vector to unit length.</summary>
    /// <returns>The normalized vector, or this one when it is already of unit length.</returns>
    /// <exception cref="InvalidOperationException">Thrown when every component is zero, which is a direction no scaling can recover.</exception>
    public EmbeddingVector Normalize()
    {
        var length = Math.Sqrt(this.components.Sum(component => (double)component * component));

        if (length == 0)
        {
            throw new InvalidOperationException(
                "A vector whose components are all zero has no direction and cannot be normalized.");
        }

        // Comparing against an exact one would renormalize almost every vector a provider already normalized, and the
        // rounding of that pass is a change with no gain. The tolerance is what distinguishes a vector that is already
        // of unit length from one shortening has visibly shrunk.
        if (Math.Abs(length - 1) <= 1e-6)
        {
            return this;
        }

        return new EmbeddingVector([.. this.components.Select(component => (float)(component / length))]);
    }
}
