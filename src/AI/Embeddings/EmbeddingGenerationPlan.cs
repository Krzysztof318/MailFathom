// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.AI.Embeddings;

/// <summary>The validated declaration a provider adapter runs on: one vector space, the endpoints that serve it, and what one call may spend.</summary>
/// <remarks>
/// <para>
/// Built once, at startup, from configuration that has already been proved consistent — every endpoint agreeing on the
/// geometry, and the geometry itself being one the database can carry. An adapter therefore never revalidates and
/// never has to decide what to do about a chain that disagrees with itself.
/// </para>
/// <para>
/// Deliberately holds no credential and no secret reference. What proves the deployment's identity to an endpoint is
/// resolved per request through <see cref="IEmbeddingCredentialSource" />, so a rotated key needs no restart and this
/// value is safe to hold for the lifetime of the process.
/// </para>
/// </remarks>
public sealed class EmbeddingGenerationPlan
{
    private EmbeddingGenerationPlan(
        IReadOnlyList<EmbeddingEndpoint> endpoints,
        bool allowTrimVectors,
        int maximumPassagesPerCall,
        TimeSpan requestTimeout)
    {
        this.Endpoints = endpoints;
        this.AllowTrimVectors = allowTrimVectors;
        this.MaximumPassagesPerCall = maximumPassagesPerCall;
        this.RequestTimeout = requestTimeout;
    }

    /// <summary>Gets the endpoints in the order they are tried, all of them serving one vector space.</summary>
    public IReadOnlyList<EmbeddingEndpoint> Endpoints { get; }

    /// <summary>Gets the geometry every vector this plan produces belongs to.</summary>
    /// <remarks>Read from the first endpoint because the chain is proved to agree; asking any other would give the same answer.</remarks>
    public EmbeddingProfileIdentity Identity => this.Endpoints[0].Identity;

    /// <summary>Gets whether a vector wider than the declared dimension may be cut down to it.</summary>
    /// <remarks>
    /// Off by default, so a model wider than the declared space is a refusal rather than a silent narrowing. It governs
    /// the runtime answer as well as the startup check: with it off, an endpoint that returns more components than the
    /// identity claims has answered in a space this deployment did not declare, and that is a failure rather than
    /// something to truncate.
    /// </remarks>
    public bool AllowTrimVectors { get; }

    /// <summary>Gets the greatest number of passages one call sends.</summary>
    /// <remarks>
    /// The batch bound. What one passage may carry is not a second setting beside it: the identity's own input
    /// character limit already states what the model sees, so a per-request ceiling here would be a second rule able
    /// to cut a passage differently from the one the profile records — producing vectors in a space nothing declared.
    /// </remarks>
    public int MaximumPassagesPerCall { get; }

    /// <summary>Gets the time one request to one endpoint may take before it is abandoned.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Builds a plan, refusing a chain that could not serve one vector space.</summary>
    /// <param name="endpoints">The endpoints in fallback order.</param>
    /// <param name="allowTrimVectors">Whether a wider vector may be cut down to the declared dimension.</param>
    /// <param name="maximumPassagesPerCall">The greatest number of passages one call sends.</param>
    /// <param name="requestTimeout">The time one request may take.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the chain is empty or its endpoints do not all declare one geometry.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is not positive.</exception>
    public static EmbeddingGenerationPlan Create(
        IReadOnlyList<EmbeddingEndpoint> endpoints,
        bool allowTrimVectors,
        int maximumPassagesPerCall,
        TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPassagesPerCall);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestTimeout, TimeSpan.Zero);

        if (endpoints.Count == 0)
        {
            throw new ArgumentException("A chain names at least one endpoint.", nameof(endpoints));
        }

        if (EmbeddingChainAgreement.FindDisagreement(endpoints) is { } disagreement)
        {
            throw new ArgumentException(disagreement, nameof(endpoints));
        }

        return new EmbeddingGenerationPlan(
            [.. endpoints],
            allowTrimVectors,
            maximumPassagesPerCall,
            requestTimeout);
    }
}
