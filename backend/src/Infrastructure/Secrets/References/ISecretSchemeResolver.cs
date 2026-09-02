// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.References;

/// <summary>Retrieves material for the one scheme an adapter serves.</summary>
/// <remarks>
/// <para>
/// This is the extension point that keeps a future Kubernetes, Azure Key Vault, HashiCorp Vault, or AWS Secrets Manager
/// integration a registration rather than a refactor. It is public — unusually for this assembly, which defaults to
/// internal — because an adapter may be declared in another folder, another project, or a later change set, and an
/// internal contract would make that impossible without editing this file.
/// </para>
/// <para>
/// Provider-specific concerns stay inside the implementation: timeouts, retry and backoff, endpoint and region
/// selection, SDK client lifetime, platform identity, and any caching policy. The contract exposes none of them, so a
/// store that must cache aggressively and a local file that must never cache coexist without it taking a position. A
/// managed store must authenticate through platform-issued identity — a managed identity, a ServiceAccount token, a
/// Vault role — because requiring MailFathom to hold a credential in order to fetch its credentials would be circular.
/// </para>
/// </remarks>
public interface ISecretSchemeResolver
{
    /// <summary>Gets the scheme this adapter serves, which is also its dispatch key.</summary>
    SecretReferenceScheme Scheme { get; }

    /// <summary>Retrieves the material the reference names.</summary>
    /// <param name="reference">The parsed reference, whose scheme equals <see cref="Scheme" />.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    /// <returns>The material, whose ownership passes to the caller, or a named failure.</returns>
    Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken);
}
