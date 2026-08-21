// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Providers;

/// <summary>Resolves what one request presents to one AI provider endpoint.</summary>
/// <remarks>
/// <para>
/// The port exists so this boundary holds no secret provider at all: references, schemes, and the resolution rules
/// governed by
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md">ADR 0005</see>
/// stay where every other credential's do, and what arrives here is already resolved material with a defined lifetime.
/// It is asked per request rather than once, which is what lets a rotated key take effect without a restart.
/// </para>
/// <para>
/// One source serves every provider role, because an alias names one endpoint across the whole deployment. Startup
/// refuses a chat endpoint that reuses an embedding endpoint's alias for exactly that reason: the alias is what a
/// credential, a resilience circuit, and every log line naming an endpoint are keyed by, and two endpoints answering to
/// one name would share all three.
/// </para>
/// </remarks>
public interface IProviderEndpointCredentialSource
{
    /// <summary>Resolves the credential configured for one endpoint.</summary>
    /// <param name="endpointAlias">The deployment's own name for the endpoint.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The credential, owned by the caller and released when the request that asked for it ends.</returns>
    /// <exception cref="ArgumentException">Thrown when the alias is blank.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the alias names no configured endpoint, or its credential cannot be resolved.</exception>
    Task<ProviderEndpointCredential> ResolveAsync(string endpointAlias, CancellationToken cancellationToken);
}
