// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Providers;

/// <summary>What one request presents to one AI provider endpoint, already resolved from whatever reference declared it.</summary>
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
public sealed class ProviderEndpointCredential : IDisposable
{
    private readonly IDisposable? resolvedMaterial;

    private ProviderEndpointCredential(
        ProviderEndpointCredentialKind kind,
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
    public ProviderEndpointCredentialKind Kind { get; }

    /// <summary>Gets the resolved provider key, or <see langword="null" /> when the credential is a Microsoft Entra one.</summary>
    public string? ApiKey { get; }

    /// <summary>Gets what the Microsoft Entra credential is built from, or <see langword="null" /> when the credential is a key.</summary>
    public EntraCredentialDeclaration? Entra { get; }

    /// <summary>Builds a credential that presents a provider-issued key.</summary>
    /// <param name="apiKey">The resolved key.</param>
    /// <param name="resolvedMaterial">The secret material the key was read from, released when this credential is, or <see langword="null" /> when the caller holds none.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentException">Thrown when the key is blank.</exception>
    public static ProviderEndpointCredential FromApiKey(string apiKey, IDisposable? resolvedMaterial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return new ProviderEndpointCredential(
            ProviderEndpointCredentialKind.ApiKey,
            apiKey,
            entra: null,
            resolvedMaterial);
    }

    /// <summary>Builds a credential that presents a Microsoft Entra access token.</summary>
    /// <param name="declaration">What the credential is built from.</param>
    /// <param name="resolvedMaterial">The secret material the declaration's own secret was read from, released when this credential is, or <see langword="null" /> when the shape holds none.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaration" /> is <see langword="null" />.</exception>
    public static ProviderEndpointCredential FromEntra(
        EntraCredentialDeclaration declaration,
        IDisposable? resolvedMaterial)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return new ProviderEndpointCredential(declaration.Kind, apiKey: null, declaration, resolvedMaterial);
    }

    /// <summary>Builds the credential of an endpoint that asks for none, which presents nothing.</summary>
    /// <returns>The credential.</returns>
    /// <remarks>
    /// A value rather than a null credential, so every caller goes on receiving one and the request that carries no
    /// authentication is a declared shape instead of a missing object nobody has to handle. It holds no material, so
    /// disposing it releases nothing.
    /// </remarks>
    public static ProviderEndpointCredential Unauthenticated() => new(
        ProviderEndpointCredentialKind.Unauthenticated,
        apiKey: null,
        entra: null,
        resolvedMaterial: null);

    /// <inheritdoc />
    public void Dispose() => this.resolvedMaterial?.Dispose();
}
