// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Embeddings;

/// <summary>What one embedding request presents to one endpoint, already resolved from whatever reference declared it.</summary>
/// <remarks>
/// <para>
/// The instance is owned by the request that resolved it and released when that request ends, which bounds the window
/// in which a process dump could hold the key to one call rather than to process uptime. A key rotated behind an
/// unchanged reference is therefore picked up by the next request, with no cache to invalidate.
/// </para>
/// <para>
/// A Microsoft Entra credential carries no material here at all. Its own secret, where the chosen shape has one, is
/// resolved by whoever builds this value and released with it; what the request presents is an access token the
/// credential fetches and caches for itself.
/// </para>
/// </remarks>
public sealed class EmbeddingEndpointCredential : IDisposable
{
    private readonly IDisposable? resolvedMaterial;

    private EmbeddingEndpointCredential(
        EmbeddingEndpointCredentialKind kind,
        string? apiKey,
        EntraCredentialDeclaration? entra,
        IDisposable? resolvedMaterial)
    {
        this.Kind = kind;
        this.ApiKey = apiKey;
        this.Entra = entra;
        this.resolvedMaterial = resolvedMaterial;
    }

    /// <summary>Gets how the deployment proves its identity to the endpoint.</summary>
    public EmbeddingEndpointCredentialKind Kind { get; }

    /// <summary>Gets the resolved provider key, or <see langword="null" /> when the credential is a Microsoft Entra one.</summary>
    public string? ApiKey { get; }

    /// <summary>Gets what the Microsoft Entra credential is built from, or <see langword="null" /> when the credential is a key.</summary>
    public EntraCredentialDeclaration? Entra { get; }

    /// <summary>Builds a credential that presents a provider-issued key.</summary>
    /// <param name="apiKey">The resolved key.</param>
    /// <param name="resolvedMaterial">The secret material the key was read from, released when this credential is, or <see langword="null" /> when the caller holds none.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentException">Thrown when the key is blank.</exception>
    public static EmbeddingEndpointCredential FromApiKey(string apiKey, IDisposable? resolvedMaterial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return new EmbeddingEndpointCredential(
            EmbeddingEndpointCredentialKind.ApiKey,
            apiKey,
            entra: null,
            resolvedMaterial);
    }

    /// <summary>Builds a credential that presents a Microsoft Entra access token.</summary>
    /// <param name="declaration">What the credential is built from.</param>
    /// <param name="resolvedMaterial">The secret material the declaration's own secret was read from, released when this credential is, or <see langword="null" /> when the shape holds none.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaration" /> is <see langword="null" />.</exception>
    public static EmbeddingEndpointCredential FromEntra(
        EntraCredentialDeclaration declaration,
        IDisposable? resolvedMaterial)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return new EmbeddingEndpointCredential(declaration.Kind, apiKey: null, declaration, resolvedMaterial);
    }

    /// <inheritdoc />
    public void Dispose() => this.resolvedMaterial?.Dispose();
}
