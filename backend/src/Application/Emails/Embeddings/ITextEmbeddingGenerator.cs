// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Turns passages of text into points of one vector space.</summary>
/// <remarks>
/// <para>
/// The whole of what an embedding provider is, as far as this application is concerned: text in, vectors out. No
/// provider type, no model name as a compile-time constant, and no provider exception crosses this boundary, which is
/// what lets a second provider be a new adapter rather than a change to a use case.
/// </para>
/// <para>
/// <see cref="Identity" /> is part of the contract rather than a convenience, because a vector means nothing without
/// the geometry it belongs to: it is what an activation computes a profile fingerprint from, and what a stored vector
/// is attributed to afterwards. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// </remarks>
public interface ITextEmbeddingGenerator
{
    /// <summary>Gets the geometry of the space every vector this generator produces belongs to.</summary>
    /// <remarks>Fixed for the lifetime of the generator. Changing the declared model is an activation rather than a mutation of this value.</remarks>
    EmbeddingProfileIdentity Identity { get; }

    /// <summary>Gets the greatest number of passages one call accepts.</summary>
    /// <remarks>
    /// Published rather than left to the caller to discover from a failure, so work is cut into calls this generator
    /// serves instead of being sent whole and refused. The bound is the deployment's, not the model's: it is what this
    /// instance is configured to send at once.
    /// </remarks>
    int MaximumPassagesPerCall { get; }

    /// <summary>Produces one vector for each passage, in the order the passages were given.</summary>
    /// <param name="passages">The passages to embed, at most <see cref="MaximumPassagesPerCall" /> of them and none of them blank.</param>
    /// <param name="cancellationToken">Cancels the call and every remaining attempt of it.</param>
    /// <returns>One vector per passage, in the same order, each of <see cref="EmbeddingProfileIdentity.Dimension" /> components.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the sequence is empty, holds a blank passage, or holds more passages than one call accepts.</exception>
    /// <exception cref="EmbeddingGenerationFailedException">Thrown when the request reached a provider and produced no vectors. Its <see cref="EmbeddingGenerationFailedException.Failure" /> says which kind of failure ended it.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancelled or the host is shutting down. Never reported as a provider failure, because neither is one.</exception>
    /// <remarks>
    /// The call is free of side effects and safe to repeat: nothing is written, and a passage embedded twice produces
    /// the same vector for every model this system supports. Preparing a passage — cutting it to what the model
    /// accepts and prefixing whatever the model requires — belongs to the implementation, because those choices are
    /// part of <see cref="Identity" /> and a caller that made them would be able to disagree with it.
    /// </remarks>
    Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken);
}
