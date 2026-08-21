// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>The geometry of one vector space, which is the whole of what an embedding profile is.</summary>
/// <remarks>
/// <para>
/// These are exactly the properties that decide whether two vectors can be compared, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// admits nothing else. The chunk boundary rules are absent by decision rather than by omission: they belong to the
/// chunk's own hash, so re-cutting a mailbox is a local derivation instead of a paid re-embed. The endpoint address, the
/// credential, the batch size, the request rate, the concurrency, and the ceilings are configuration, because none of
/// them changes what a vector means.
/// </para>
/// <para>
/// <see cref="Dimension" /> is a member rather than something derived from the model, for two reasons: some providers
/// take it as a request parameter, and where a model is narrowed to what the database can index, the narrowed width is
/// what the stored vectors have. A profile claiming the model's nominal dimension would be describing vectors that do
/// not exist.
/// </para>
/// <para>
/// Deliberately not a record. Two identities are compared through <see cref="EmbeddingProfileFingerprint" />, which is
/// what the profile table's unique index is over, and a second equality mechanism beside it would be a second answer to
/// one question.
/// </para>
/// </remarks>
public sealed class EmbeddingProfileIdentity
{
    /// <summary>The greatest number of characters a provider name may hold.</summary>
    public const int MaximumProviderLength = 64;

    /// <summary>The greatest number of characters a provider's model identifier may hold.</summary>
    public const int MaximumModelIdentifierLength = 128;

    /// <summary>The greatest number of characters a model version may hold.</summary>
    public const int MaximumModelVersionLength = 64;

    private EmbeddingProfileIdentity(
        string provider,
        string modelIdentifier,
        string? modelVersion,
        int dimension,
        EmbeddingDistanceMetric distanceMetric,
        EmbeddingInputPreparation inputPreparation)
    {
        this.Provider = provider;
        this.ModelIdentifier = modelIdentifier;
        this.ModelVersion = modelVersion;
        this.Dimension = dimension;
        this.DistanceMetric = distanceMetric;
        this.InputPreparation = inputPreparation;
    }

    /// <summary>Gets the vendor whose model defines this space.</summary>
    /// <remarks>
    /// The vendor, not the endpoint the model is reached through. That distinction is what lets one profile be served by
    /// a chain of endpoints — the same model offered by a first-party API and by a cloud deployment of it — where the
    /// endpoint can fail over without the vector space changing underneath the vectors already stored. The value is a
    /// vendor-supplied name that MailFathom stores verbatim rather than an enumeration of its own, for the same reason
    /// <see cref="ModelIdentifier" /> is: neither set is MailFathom's to close.
    /// </remarks>
    public string Provider { get; }

    /// <summary>Gets the model identifier the provider publishes.</summary>
    public string ModelIdentifier { get; }

    /// <summary>Gets the model version the provider exposes, or <see langword="null" /> where it exposes none.</summary>
    /// <remarks>
    /// Null is a provider that versions nothing, which is a different statement from a version nobody recorded. Most
    /// providers replace a model rather than version it, so absence is the ordinary case.
    /// </remarks>
    public string? ModelVersion { get; }

    /// <summary>Gets the width of the vectors this space holds.</summary>
    public int Dimension { get; }

    /// <summary>Gets how distance is measured between two vectors of this space.</summary>
    public EmbeddingDistanceMetric DistanceMetric { get; }

    /// <summary>Gets what is done to a passage before it is sent, which decides what the model saw.</summary>
    public EmbeddingInputPreparation InputPreparation { get; }

    /// <summary>Builds an identity, refusing one that could not describe a vector space.</summary>
    /// <param name="provider">The vendor whose model defines the space.</param>
    /// <param name="modelIdentifier">The model identifier the provider publishes.</param>
    /// <param name="modelVersion">The model version the provider exposes, or <see langword="null" />.</param>
    /// <param name="dimension">The width of the vectors this space holds.</param>
    /// <param name="distanceMetric">How distance is measured between two of its vectors.</param>
    /// <param name="inputPreparation">What is done to a passage before it is sent.</param>
    /// <returns>The profile identity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputPreparation" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a name is blank or longer than the bound its column carries.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the dimension is not positive.</exception>
    public static EmbeddingProfileIdentity Create(
        string provider,
        string modelIdentifier,
        string? modelVersion,
        int dimension,
        EmbeddingDistanceMetric distanceMetric,
        EmbeddingInputPreparation inputPreparation)
    {
        ArgumentNullException.ThrowIfNull(inputPreparation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        RequireName(provider, MaximumProviderLength, nameof(provider));
        RequireName(modelIdentifier, MaximumModelIdentifierLength, nameof(modelIdentifier));

        if (modelVersion is not null)
        {
            RequireName(modelVersion, MaximumModelVersionLength, nameof(modelVersion));
        }

        return new EmbeddingProfileIdentity(
            provider,
            modelIdentifier,
            modelVersion,
            dimension,
            distanceMetric,
            inputPreparation);
    }

    private static void RequireName(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"The value is at most {maximumLength} characters.", parameterName);
        }
    }
}
