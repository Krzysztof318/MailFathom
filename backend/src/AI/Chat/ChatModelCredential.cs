// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Providers;
using MailFathom.Application.Chat;

namespace MailFathom.AI.Chat;

/// <summary>Opens one model's credential, stating a refusal as that model's own failure.</summary>
/// <remarks>
/// <para>
/// The credential source publishes one failure — the alias names no endpoint the configuration in force declares, or
/// the secret behind it did not resolve — and publishes it as an <see cref="InvalidOperationException" />. Raised as
/// itself it reaches <see cref="ChatModelFallThrough" /> as something that type cannot read, so the chain ends on the
/// model it happened to, and a declared fallback is never tried for exactly the shape of failure it exists for: an
/// endpoint this deployment cannot currently use, where the fallback is a second address under a second credential.
/// </para>
/// <para>
/// <see cref="ChatGenerationFailure.CredentialRejected" /> is what it becomes, because the operator's action is the one
/// that classification already names — rotate or correct the credential — and because it says the same thing about
/// repeating: nothing changes until somebody acts, so the endpoint is recorded as misconfigured rather than as
/// briefly unavailable.
/// </para>
/// </remarks>
internal static class ChatModelCredential
{
    /// <summary>Resolves the credential one request to this model presents.</summary>
    /// <param name="source">Where a declared alias's credential is resolved from.</param>
    /// <param name="endpoint">The model the credential is being opened for.</param>
    /// <param name="cancellationToken">Withdraws the resolution.</param>
    /// <returns>The credential, which the caller releases with the request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ChatGenerationFailedException">Thrown when nothing could be resolved for this model.</exception>
    public static async Task<ProviderEndpointCredential> ResolveAsync(
        IProviderEndpointCredentialSource source,
        ChatEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(endpoint);

        try
        {
            return await source.ResolveAsync(endpoint.Alias, cancellationToken);
        }
        catch (InvalidOperationException refusal)
        {
            throw new ChatGenerationFailedException(
                endpoint.Alias,
                ChatGenerationFailure.CredentialRejected,
                refusal);
        }
    }
}
