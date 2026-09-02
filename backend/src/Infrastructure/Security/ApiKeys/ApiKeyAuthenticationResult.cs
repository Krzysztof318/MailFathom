// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Security.ApiKeys;

/// <summary>The outcome of judging one presented MCP credential against the configured API keys.</summary>
/// <remarks>
/// A refused credential is an expected outcome of serving an open endpoint rather than an exceptional state, so
/// authentication returns this instead of throwing. The successful result carries the key's name and nothing else:
/// that name is what an audit record and a diagnostic correlate on, and it is the only part of a key that may be
/// written down.
/// </remarks>
public sealed record ApiKeyAuthenticationResult
{
    private ApiKeyAuthenticationResult(SecretName? authenticatedKeyName, ApiKeyRejection? rejection)
    {
        this.AuthenticatedKeyName = authenticatedKeyName;
        this.Rejection = rejection;
    }

    /// <summary>Gets whether the presented credential authenticated.</summary>
    public bool Succeeded => this.AuthenticatedKeyName is not null;

    /// <summary>Gets the name of the key that matched, or <see langword="null" /> when the credential was refused.</summary>
    public SecretName? AuthenticatedKeyName { get; }

    /// <summary>Gets why the credential was refused, or <see langword="null" /> when it authenticated.</summary>
    /// <remarks>It reaches the server log only. Every value produces one indistinguishable response.</remarks>
    public ApiKeyRejection? Rejection { get; }

    /// <summary>Creates a successful result naming the key that matched.</summary>
    /// <param name="authenticatedKeyName">The name of the matching key.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="authenticatedKeyName" /> is the unspecified struct default.</exception>
    public static ApiKeyAuthenticationResult Authenticated(SecretName authenticatedKeyName) =>
        authenticatedKeyName.IsSpecified
            ? new ApiKeyAuthenticationResult(authenticatedKeyName, rejection: null)
            : throw new ArgumentException(
                "An authenticated result must name the key that matched.",
                nameof(authenticatedKeyName));

    /// <summary>Creates a refused result.</summary>
    /// <param name="rejection">Why the credential was refused.</param>
    /// <returns>The refused result.</returns>
    public static ApiKeyAuthenticationResult Rejected(ApiKeyRejection rejection) =>
        new(authenticatedKeyName: null, rejection);
}
