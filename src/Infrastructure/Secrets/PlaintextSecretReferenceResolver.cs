// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Resolves <c>plaintext:&lt;value&gt;</c> as the literal that follows the scheme.</summary>
/// <remarks>
/// The scheme is the unambiguous spelling for a literal that would otherwise look like a reference — a password whose
/// value genuinely begins with <c>file:</c> — and the convenient shape for local development. It carries no environment
/// gate of its own: what protects a deployment is that <see cref="SecretValueInterpretation.ReferenceOnly" /> is the
/// default and any other mode is a deliberate, logged setting. Like every inline value, the literal reaches this
/// adapter as a <see cref="string" /> that cannot be erased.
/// </remarks>
internal sealed class PlaintextSecretReferenceResolver : ISecretSchemeResolver
{
    /// <inheritdoc />
    public SecretReferenceScheme Scheme => SecretReferenceScheme.Plaintext;

    /// <inheritdoc />
    public Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return Task.FromResult(SecretResolutionResult.Resolved(
            ResolvedSecret.FromText(reference.Target),
            SecretMaterialSource.SchemeAdapter));
    }
}
