// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Embeddings;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Resolves the credential one embedding request presents, from the references the endpoint declared.</summary>
/// <remarks>
/// The adapter exists so the AI boundary holds no secret provider at all: references, schemes, and the resolution rules
/// stay in the composition root, and what crosses is material with a defined lifetime. It resolves per request rather
/// than once, so a key rotated behind an unchanged reference takes effect on the next call with no cache to invalidate.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this credential source.")]
internal sealed class ConfiguredEmbeddingCredentialSource(
    IOptions<EmbeddingOptions> embeddingSettings,
    ISecretReferenceResolver secretReferenceResolver) : IEmbeddingCredentialSource
{
    /// <inheritdoc />
    public async Task<EmbeddingEndpointCredential> ResolveAsync(
        string endpointAlias,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointAlias);

        var endpoint = embeddingSettings.Value.Endpoints
            .FirstOrDefault(candidate => string.Equals(candidate.Alias.Trim(), endpointAlias, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Embedding endpoint '{endpointAlias}' is not present in the configuration this deployment started with.");

        return endpoint.EntraCredential is { } entra
            ? await this.ResolveEntraCredentialAsync(endpointAlias, entra, cancellationToken)
            : await this.ResolveApiKeyAsync(endpointAlias, endpoint.ApiKey, cancellationToken);
    }

    private async Task<EmbeddingEndpointCredential> ResolveApiKeyAsync(
        string endpointAlias,
        ConfiguredSecret? apiKey,
        CancellationToken cancellationToken)
    {
        var material = await this.ResolveMaterialAsync(endpointAlias, apiKey, "provider key", cancellationToken);

        try
        {
            // Revealed as late as possible and handed straight to the client the request is sent with, which is the one
            // boundary that takes a string. The buffer it came from is released with the credential.
            return EmbeddingEndpointCredential.FromApiKey(material!.RevealAsString(), material);
        }
        catch
        {
            material!.Dispose();

            throw;
        }
    }

    private async Task<EmbeddingEndpointCredential> ResolveEntraCredentialAsync(
        string endpointAlias,
        EmbeddingEntraCredentialOptions entra,
        CancellationToken cancellationToken)
    {
        // At most one shape carries a secret, so at most one is resolved and there is never a second buffer to release
        // if the first one fails.
        var clientSecret = entra.Kind is EmbeddingEndpointCredentialKind.ClientSecret
            ? await this.ResolveMaterialAsync(endpointAlias, entra.ClientSecret, "application secret", cancellationToken)
            : null;

        var certificatePassword = entra.Kind is EmbeddingEndpointCredentialKind.ClientCertificate && entra.CertificatePassword is not null
            ? await this.ResolveMaterialAsync(endpointAlias, entra.CertificatePassword, "certificate password", cancellationToken)
            : null;

        var material = clientSecret ?? certificatePassword;

        try
        {
            return EmbeddingEndpointCredential.FromEntra(
                new EntraCredentialDeclaration(
                    entra.Kind,
                    entra.TokenScope.Trim(),
                    NullWhenEmpty(entra.TenantId),
                    NullWhenEmpty(entra.ClientId),
                    clientSecret?.RevealAsString(),
                    NullWhenEmpty(entra.CertificatePath),
                    certificatePassword?.RevealAsString()),
                material);
        }
        catch
        {
            material?.Dispose();

            throw;
        }
    }

    private async Task<ResolvedSecret?> ResolveMaterialAsync(
        string endpointAlias,
        ConfiguredSecret? declaration,
        string secretDescription,
        CancellationToken cancellationToken)
    {
        var resolution = await secretReferenceResolver.ResolveAsync(declaration?.SecretReference, cancellationToken);

        // The failure names the kind of secret and the endpoint's alias and nothing else: the target of a reference is
        // a path or a store identifier, which resolution deliberately keeps out of its own result for the same reason.
        return resolution.Secret ?? throw new InvalidOperationException(
            $"The {secretDescription} of embedding endpoint '{endpointAlias}' could not be resolved [{resolution.Failure}].");
    }

    private static string? NullWhenEmpty(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
