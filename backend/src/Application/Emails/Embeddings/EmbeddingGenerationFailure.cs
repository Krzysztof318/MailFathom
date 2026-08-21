// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Names why an embedding request did not produce vectors, at the granularity a caller acts on.</summary>
/// <remarks>
/// <para>
/// The set exists because the four remote failures below look identical to a caller that only sees "the provider
/// failed", and they are not: two of them are worth repeating and two are not. A rate limit answered with an immediate
/// retry is how an account gets throttled harder, and a refused credential repeated is how the same refusal is bought
/// again, so the classification is the deliverable rather than a detail of one.
/// </para>
/// <para>
/// A caller's own cancellation and a host shutdown are absent by design. Both arrive as
/// <see cref="OperationCanceledException" /> and neither is a statement about the provider, so folding them in here
/// would put a decision this system made among the answers a remote party gave.
/// </para>
/// </remarks>
public enum EmbeddingGenerationFailure
{
    /// <summary>The provider refused the credential the deployment presented.</summary>
    /// <remarks>Terminal. Every repetition receives the same answer while counting against the account's request budget.</remarks>
    CredentialRejected = 0,

    /// <summary>The provider refused the request because the deployment is over its allowed rate.</summary>
    /// <remarks>Worth repeating, but only after a backoff. It is separate from a transport fault because the provider answered rather than failed to answer, and answering again immediately is what turns a throttle into a longer one.</remarks>
    RateLimited = 1,

    /// <summary>The request outlived the time the deployment allows one embedding call.</summary>
    RequestTimedOut = 2,

    /// <summary>The request never reached an answer: the endpoint was unreachable, the connection dropped, or the response was unreadable.</summary>
    TransportFaulted = 3,

    /// <summary>The provider rejected the request itself, for a reason repeating it cannot change.</summary>
    /// <remarks>A model the deployment does not serve, an input beyond what the model accepts, a malformed argument. Terminal, and the one classification that names a declaration to correct rather than a remote condition to wait out.</remarks>
    RequestRefused = 4,

    /// <summary>The provider answered with a vector the declared geometry does not describe.</summary>
    /// <remarks>A width other than the declared dimension, a count that does not match the passages sent, or a component that is not a finite number.</remarks>
    VectorShapeUnexpected = 5,
}
