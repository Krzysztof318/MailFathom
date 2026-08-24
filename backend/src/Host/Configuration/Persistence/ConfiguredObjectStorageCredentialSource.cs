// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Resolves the access key one request to the object-storage endpoint is signed with, from the references the section declared.</summary>
/// <remarks>
/// <para>
/// The adapter exists so the object-storage boundary holds no secret machinery at all: references, schemes, and the
/// resolution rules stay in the composition root, and what crosses is material with a defined lifetime. It resolves per
/// request rather than once, so a key rotated behind an unchanged reference takes effect on the next call with no cache
/// to invalidate.
/// </para>
/// <para>
/// Both halves are resolved, and either failing refuses the operation rather than falling back. That is the whole point:
/// the AWS client's own credential chain reaches environment variables, a shared credentials file, and an instance
/// metadata service, and a deployment that lost its credential must fail instead of quietly signing as whatever identity
/// the host happens to carry.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this credential source.")]
internal sealed class ConfiguredObjectStorageCredentialSource(
    IOptions<ContentStorageOptions> contentStorageSettings,
    ISecretReferenceResolver secretReferenceResolver) : IObjectStorageCredentialSource
{
    /// <inheritdoc />
    public async Task<ObjectStorageCredential> ResolveAsync(CancellationToken cancellationToken)
    {
        var declaration = contentStorageSettings.Value.ObjectStorage;

        var accessKeyId = await this.ResolveMaterialAsync(
            nameof(ObjectStorageOptions.AccessKeyId),
            declaration.AccessKeyId,
            cancellationToken);

        try
        {
            var secretAccessKey = await this.ResolveMaterialAsync(
                nameof(ObjectStorageOptions.SecretAccessKey),
                declaration.SecretAccessKey,
                cancellationToken);

            // Ownership of both buffers passes to the credential, which erases them together when the operation that
            // holds it ends.
            return ObjectStorageCredential.Create(accessKeyId, secretAccessKey);
        }
        catch
        {
            accessKeyId.Dispose();

            throw;
        }
    }

    private async Task<ResolvedSecret> ResolveMaterialAsync(
        string propertyName,
        ConfiguredSecret? declaration,
        CancellationToken cancellationToken)
    {
        var resolution = await secretReferenceResolver.ResolveAsync(declaration?.SecretReference, cancellationToken);

        // The failure names the setting and nothing else: the target of a reference is a path or a store identifier,
        // which resolution deliberately keeps out of its own result for the same reason.
        return resolution.Secret ?? throw new InvalidOperationException(
            $"{ObjectStorageOptions.SectionPath}:{propertyName} could not be resolved [{resolution.Failure}].");
    }
}
