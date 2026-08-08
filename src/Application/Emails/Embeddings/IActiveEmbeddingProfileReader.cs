// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Answers which vector space this instance is currently embedding into, if any.</summary>
/// <remarks>
/// A read of committed local state and nothing else: it reaches no provider, so an instance whose provider is
/// unreachable still knows what it would be embedding under. The port is deliberately read-only, because activation is
/// the only writer of a profile and it belongs to an explicit operator command rather than to any running use case.
/// </remarks>
public interface IActiveEmbeddingProfileReader
{
    /// <summary>Reads the profile retrieval and generation currently work under.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The active profile, or <see langword="null" /> when this instance has activated none.</returns>
    /// <remarks>
    /// <see langword="null" /> is an ordinary answer rather than a failure. An instance that has activated no profile
    /// embeds nothing, serves lexical search, and is a supported deployment.
    /// </remarks>
    Task<RegisteredEmbeddingProfile?> FindActiveProfileAsync(CancellationToken cancellationToken);
}
