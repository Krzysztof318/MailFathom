// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Resolves <c>plaintext:</c> references and fails every other scheme, without touching the file system or the environment block.</summary>
/// <remarks>
/// Host tests are about what the host does with a resolution outcome, not about how a scheme retrieves material, so the
/// adapters stay covered by <c>Infrastructure.UnitTests</c> and this double keeps the outcome deterministic.
/// </remarks>
internal sealed class PlaintextOnlySecretReferenceResolver : ISecretReferenceResolver
{
    /// <summary>Gets or sets the provenance reported for a successful resolution.</summary>
    public SecretMaterialSource Source { get; set; } = SecretMaterialSource.SchemeAdapter;

    public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
    {
        if (!SecretReference.TryParse(configuredValue, out var reference, out var grammarFailure))
        {
            return Task.FromResult(SecretResolutionResult.Failed(grammarFailure));
        }

        return Task.FromResult(reference.Scheme == SecretReferenceScheme.Plaintext
            ? SecretResolutionResult.Resolved(ResolvedSecret.FromText(reference.Target), this.Source)
            : SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound));
    }
}
