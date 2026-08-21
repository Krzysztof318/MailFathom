// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Builds the endpoints and plans the embedding tests declare, so each test states only what it varies.</summary>
internal static class EmbeddingDeclarations
{
    /// <summary>The width every endpoint below produces vectors in unless a test says otherwise.</summary>
    public const int Dimension = 4;

    /// <summary>Builds one endpoint of a chain.</summary>
    public static EmbeddingEndpoint Endpoint(
        string alias = "primary",
        string provider = "openai",
        string model = "text-embedding-3-small",
        string? modelVersion = null,
        int dimension = Dimension,
        EmbeddingDistanceMetric distanceMetric = EmbeddingDistanceMetric.Cosine,
        int inputCharacterLimit = 8000,
        string? passageInstruction = null,
        bool normalizesVector = true,
        string? address = "https://provider.invalid/v1/",
        string? routedModelName = null,
        bool supportsRequestedDimension = true) =>
        new(
            alias,
            EmbeddingProfileIdentity.Create(
                provider,
                model,
                modelVersion,
                dimension,
                distanceMetric,
                EmbeddingInputPreparation.Create(inputCharacterLimit, passageInstruction, normalizesVector)),
            address is null ? null : new Uri(address, UriKind.Absolute),
            routedModelName ?? model,
            supportsRequestedDimension);

    /// <summary>Builds a plan over a chain.</summary>
    public static EmbeddingGenerationPlan Plan(
        bool allowTrimVectors = false,
        int maximumPassagesPerCall = 16,
        params EmbeddingEndpoint[] endpoints) =>
        EmbeddingGenerationPlan.Create(
            endpoints.Length > 0 ? endpoints : [Endpoint()],
            allowTrimVectors,
            maximumPassagesPerCall,
            TimeSpan.FromSeconds(5));
}
