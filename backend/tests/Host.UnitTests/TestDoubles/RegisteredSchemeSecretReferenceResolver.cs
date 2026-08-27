// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Resolves a reference under any scheme a deployment registers, and refuses every other one.</summary>
/// <remarks>
/// <para>
/// It answers the question a start asks of material that is not the deployment's own: whether the reference an owner
/// carries reaches something. A persisted document may not carry a literal, so a test about one cannot state its
/// secret as <c>plaintext:</c>, and <see cref="PlaintextOnlySecretReferenceResolver" /> is therefore the wrong double
/// for it — this one resolves the schemes <see cref="DeclaredSecretScheme.Registered" /> names and nothing else, which
/// keeps a reference under an unserved scheme refused.
/// </para>
/// <para>The reference's target is handed back as the material, because what the material is never decides anything here.</para>
/// </remarks>
internal sealed class RegisteredSchemeSecretReferenceResolver : ISecretReferenceResolver
{
    public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
    {
        if (!SecretReference.TryParse(configuredValue, out var reference, out var grammarFailure))
        {
            return Task.FromResult(SecretResolutionResult.Failed(grammarFailure));
        }

        var served = DeclaredSecretScheme.Registered.Any(adapter => adapter.Scheme == reference.Scheme);

        return Task.FromResult(served
            ? SecretResolutionResult.Resolved(ResolvedSecret.FromText(reference.Target), SecretMaterialSource.SchemeAdapter)
            : SecretResolutionResult.Failed(SecretResolutionFailure.SchemeNotSupported));
    }
}
