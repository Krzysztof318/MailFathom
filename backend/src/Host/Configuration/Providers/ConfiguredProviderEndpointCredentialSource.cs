// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.Providers;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>Resolves the credential one AI provider request presents, from the references the endpoint declared.</summary>
/// <remarks>
/// <para>
/// The adapter exists so the AI boundary holds no secret provider at all: references, schemes, and the resolution rules
/// stay in the composition root, and what crosses is material with a defined lifetime. It resolves per request rather
/// than once, so a key rotated behind an unchanged reference takes effect on the next call with no cache to invalidate.
/// </para>
/// <para>
/// One source for both declared sections, keyed by the alias alone. Startup refuses a chat model whose alias an
/// embedding endpoint already uses, and so does every reloaded chat declaration, which is what lets the lookup stay a
/// search over one name rather than a name paired with the section it came from — and the same rule is what keeps two
/// endpoints from sharing one resilience circuit and one log identity.
/// </para>
/// <para>
/// The chat declaration is read from the published snapshot and the embedding chain from the composed options, because
/// that is what each of them is: the declared models are reloadable down to their aliases, so a lookup reading the
/// startup value would fail to find a model an operator renamed or added, while the embedding chain is read once while
/// the host composes itself and takes a restart to change.
/// </para>
/// <para>
/// What it resolves is everything a request presents rather than the credential alone: a chat model may declare headers
/// whose values are secret references, and those are resolved here and released with the credential so the two halves
/// have one lifetime and neither outlives the request.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this credential source.")]
internal sealed class ConfiguredProviderEndpointCredentialSource(
    IOptions<EmbeddingOptions> embeddingSettings,
    ISettingsSnapshot<ChatModelOptions> chatSettings,
    ISecretReferenceResolver secretReferenceResolver) : IProviderEndpointCredentialSource
{
    /// <inheritdoc />
    public async Task<ProviderEndpointCredential> ResolveAsync(string endpointAlias, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointAlias);

        var declaration = this.FindDeclaration(endpointAlias)
            ?? throw new InvalidOperationException(
                $"AI endpoint '{endpointAlias}' is not present in the configuration currently in force.");

        // Resolved before the credential and released with it, so the two halves of what a request presents have one
        // lifetime. A failure here leaves nothing to clean up, because the values are resolved into the list that the
        // credential takes ownership of only once every one of them succeeded.
        var headers = await this.ResolveHeadersAsync(endpointAlias, declaration.ExtraHeaders, cancellationToken);

        try
        {
            if (declaration.Unauthenticated)
            {
                // Nothing to resolve for the credential itself: the endpoint asked for none, so the request presents no
                // authentication and whatever headers it declared. Startup already refused this beside a key or a
                // Microsoft Entra credential, so reading it first decides the shape rather than competing with them.
                return ProviderEndpointCredential.Unauthenticated(headers.Values, headers.Material);
            }

            return declaration.Entra is { } entra
                ? await this.ResolveEntraCredentialAsync(endpointAlias, entra, headers, cancellationToken)
                : await this.ResolveApiKeyAsync(endpointAlias, declaration.ApiKey, headers, cancellationToken);
        }
        catch
        {
            foreach (var material in headers.Material)
            {
                material.Dispose();
            }

            throw;
        }
    }

    /// <summary>Finds the endpoint an alias names, in whichever section declared it.</summary>
    /// <remarks>
    /// The embedding chain is searched before the declared chat models for no reason beyond having to be searched in
    /// some order. The order decides nothing, since an alias declared in both is refused at startup.
    /// </remarks>
    private ProviderCredentialDeclaration? FindDeclaration(string endpointAlias)
    {
        var embeddingEndpoint = embeddingSettings.Value.Endpoints.FirstOrDefault(
            candidate => NamesEndpoint(candidate.Alias, endpointAlias));

        if (embeddingEndpoint is not null)
        {
            return new ProviderCredentialDeclaration(
                embeddingEndpoint.ApiKey,
                embeddingEndpoint.EntraCredential,
                embeddingEndpoint.Unauthenticated,
                []);
        }

        return chatSettings.Current.FindModel(endpointAlias) is { } model
            ? new ProviderCredentialDeclaration(
                model.ApiKey,
                model.EntraCredential,
                model.Unauthenticated,
                [.. model.ExtraHeaders])
            : null;
    }

    /// <summary>Resolves every header this endpoint declared, or releases what it had resolved and reports the first that could not be.</summary>
    private async Task<ResolvedHeaders> ResolveHeadersAsync(
        string endpointAlias,
        IReadOnlyList<ChatModelHeaderOptions> declarations,
        CancellationToken cancellationToken)
    {
        if (declarations.Count == 0)
        {
            return new ResolvedHeaders([], []);
        }

        var values = new List<ProviderEndpointHeader>(declarations.Count);
        var material = new List<IDisposable>(declarations.Count);

        try
        {
            foreach (var declaration in declarations)
            {
                var resolved = await this.ResolveMaterialAsync(
                    endpointAlias,
                    declaration.Value,
                    $"'{declaration.Name.Trim()}' header",
                    cancellationToken);

                material.Add(resolved!);
                values.Add(new ProviderEndpointHeader(declaration.Name.Trim(), resolved!.RevealAsString()));
            }
        }
        catch
        {
            foreach (var resolved in material)
            {
                resolved.Dispose();
            }

            throw;
        }

        return new ResolvedHeaders(values, material);
    }

    private async Task<ProviderEndpointCredential> ResolveApiKeyAsync(
        string endpointAlias,
        ConfiguredSecret? apiKey,
        ResolvedHeaders headers,
        CancellationToken cancellationToken)
    {
        var material = await this.ResolveMaterialAsync(endpointAlias, apiKey, "provider key", cancellationToken);

        try
        {
            // Revealed as late as possible and handed straight to the client the request is sent with, which is the one
            // boundary that takes a string. The buffer it came from is released with the credential.
            return ProviderEndpointCredential.FromApiKey(
                material!.RevealAsString(),
                material,
                headers.Values,
                headers.Material);
        }
        catch
        {
            material!.Dispose();

            throw;
        }
    }

    private async Task<ProviderEndpointCredential> ResolveEntraCredentialAsync(
        string endpointAlias,
        ProviderEntraCredentialOptions entra,
        ResolvedHeaders headers,
        CancellationToken cancellationToken)
    {
        // At most one shape carries a secret, so at most one is resolved and there is never a second buffer to release
        // if the first one fails.
        var clientSecret = entra.Kind is ProviderEndpointCredentialKind.ClientSecret
            ? await this.ResolveMaterialAsync(endpointAlias, entra.ClientSecret, "application secret", cancellationToken)
            : null;

        var certificatePassword = entra.Kind is ProviderEndpointCredentialKind.ClientCertificate && entra.CertificatePassword is not null
            ? await this.ResolveMaterialAsync(endpointAlias, entra.CertificatePassword, "certificate password", cancellationToken)
            : null;

        var material = clientSecret ?? certificatePassword;

        try
        {
            return ProviderEndpointCredential.FromEntra(
                new EntraCredentialDeclaration(
                    entra.Kind,
                    entra.TokenScope.Trim(),
                    NullWhenEmpty(entra.TenantId),
                    NullWhenEmpty(entra.ClientId),
                    clientSecret?.RevealAsString(),
                    NullWhenEmpty(entra.CertificatePath),
                    certificatePassword?.RevealAsString()),
                material,
                headers.Values,
                headers.Material);
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
            $"The {secretDescription} of AI endpoint '{endpointAlias}' could not be resolved [{resolution.Failure}].");
    }

    private static bool NamesEndpoint(string declaredAlias, string endpointAlias) =>
        string.Equals(declaredAlias.Trim(), endpointAlias, StringComparison.OrdinalIgnoreCase);

    private static string? NullWhenEmpty(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    /// <summary>The three credential shapes an endpoint of either section chooses between, and whatever else its requests carry, once the section it came from stops mattering.</summary>
    /// <remarks>The headers are empty for every embedding endpoint, because only a chat model declares them — which is a property of what each section may say rather than a limitation here.</remarks>
    private sealed record ProviderCredentialDeclaration(
        ConfiguredSecret? ApiKey,
        ProviderEntraCredentialOptions? Entra,
        bool Unauthenticated,
        IReadOnlyList<ChatModelHeaderOptions> ExtraHeaders);

    /// <summary>The headers of one request, resolved, beside the material each was read from.</summary>
    /// <remarks>Two lists rather than one, because a header value is a string by the time it reaches a request while the buffer it was revealed from is what has to be released afterwards.</remarks>
    private sealed record ResolvedHeaders(
        IReadOnlyList<ProviderEndpointHeader> Values,
        IReadOnlyList<IDisposable> Material);
}
