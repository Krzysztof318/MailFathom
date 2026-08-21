// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.Secrets.References;

/// <summary>Resolves <c>file:&lt;path&gt;</c> against a deployment-provisioned protected file.</summary>
/// <remarks>
/// This scheme also serves container and Kubernetes deployments: Docker and Podman Compose mount secrets at
/// <c>/run/secrets/&lt;name&gt;</c> and Kubernetes mounts a Secret as a read-only tmpfs directory holding one file per
/// key. A <c>docker-secret:</c> or <c>kubernetes-secret:</c> scheme would perform exactly this read, so neither exists.
/// </remarks>
internal sealed class FileSecretReferenceResolver(ISecretFileReader secretFileReader) : ISecretSchemeResolver
{
    /// <inheritdoc />
    public SecretReferenceScheme Scheme => SecretReferenceScheme.File;

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return secretFileReader.ReadAsync(
            reference.Target,
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellationToken);
    }
}
