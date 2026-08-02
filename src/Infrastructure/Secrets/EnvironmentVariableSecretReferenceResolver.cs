// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Secrets;

/// <summary>Resolves <c>env:&lt;variable&gt;</c> against the process environment block.</summary>
/// <remarks>
/// <para>
/// The platform returns the value as a <see cref="string" />, which cannot be erased, so an environment-sourced secret
/// leaves an un-erasable copy behind exactly as an inline value does. That is a documented residual exposure and the
/// reason the operations documentation recommends against this scheme outside non-production automation. It is not
/// gated on the environment, because the interpretation mode rather than the hosting environment is what governs how
/// permissive a deployment is.
/// </para>
/// <para>
/// The value is bounded like every other secret. An environment block is not a size an operator controls as
/// deliberately as a provisioned file, and pinning an oversized copy of one is the same failure whatever supplied it.
/// </para>
/// </remarks>
internal sealed class EnvironmentVariableSecretReferenceResolver(IEnvironmentVariableReader environmentVariableReader)
    : ISecretSchemeResolver
{
    /// <inheritdoc />
    public SecretReferenceScheme Scheme => SecretReferenceScheme.EnvironmentVariable;

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var value = environmentVariableReader.GetValue(reference.Target);

        return Task.FromResult(value switch
        {
            null => SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound),
            { Length: 0 } => SecretResolutionResult.Failed(SecretResolutionFailure.MaterialEmpty),
            _ when SecretMaterialLimits.ExceedsMaximumByteCount(value) =>
                SecretResolutionResult.Failed(SecretResolutionFailure.MaterialTooLarge),
            _ => SecretResolutionResult.Resolved(ResolvedSecret.FromText(value), SecretMaterialSource.SchemeAdapter),
        });
    }
}
