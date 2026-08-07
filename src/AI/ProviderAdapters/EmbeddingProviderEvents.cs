// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Records what an embedding call decided, and nothing about what it was carrying.</summary>
/// <remarks>
/// Every parameter here is either a name the operator chose, a classification of this system's own, or a count. No
/// passage, no vector, no credential, and no provider response body reaches these events, which is what lets them stay
/// on in a deployment holding real mail: a provider's own error text quotes the request that produced it, and the
/// request is mail text.
/// </remarks>
internal static partial class EmbeddingProviderEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Embedding endpoint {EndpointAlias} failed with {EmbeddingFailure}; falling through to the next endpoint of the chain, which reaches the same vector space.")]
    internal static partial void LogFallingThrough(
        ILogger logger,
        string endpointAlias,
        EmbeddingGenerationFailure embeddingFailure);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "No endpoint of the embedding chain served the request; {EndpointCount} were tried and the last failed with {EmbeddingFailure}.")]
    internal static partial void LogChainExhausted(
        ILogger logger,
        int endpointCount,
        EmbeddingGenerationFailure embeddingFailure);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "Embedding endpoint {EndpointAlias} returned {ReturnedDimension} components where the profile records {ProfileDimension}; the vectors were shortened and renormalized.")]
    internal static partial void LogVectorsShortened(
        ILogger logger,
        string endpointAlias,
        int returnedDimension,
        int profileDimension);
}
