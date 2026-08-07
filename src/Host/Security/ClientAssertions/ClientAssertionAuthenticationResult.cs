// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>The outcome of judging one presented client assertion against the configured public keys.</summary>
/// <remarks>
/// A refused credential is an expected outcome of serving an open endpoint rather than an exceptional state, so
/// verification returns this instead of throwing. The successful result carries the key's name and nothing else: that
/// name is what an audit record, a diagnostic, and the rate-limit partition correlate on, and nothing about the
/// assertion itself is worth keeping once it has been judged.
/// </remarks>
internal sealed record ClientAssertionAuthenticationResult
{
    private ClientAssertionAuthenticationResult(SecretName? authenticatedKeyName, ClientAssertionRejection? rejection)
    {
        this.AuthenticatedKeyName = authenticatedKeyName;
        this.Rejection = rejection;
    }

    /// <summary>Gets whether the presented assertion authenticated.</summary>
    public bool Succeeded => this.AuthenticatedKeyName is not null;

    /// <summary>Gets the name of the public key that verified the signature, or <see langword="null" /> when the assertion was refused.</summary>
    public SecretName? AuthenticatedKeyName { get; }

    /// <summary>Gets why the assertion was refused, or <see langword="null" /> when it authenticated.</summary>
    /// <remarks>It reaches the server log only. Every value produces one indistinguishable response.</remarks>
    public ClientAssertionRejection? Rejection { get; }

    /// <summary>Creates a successful result naming the key that verified the signature.</summary>
    /// <param name="authenticatedKeyName">The name of the verifying key.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="authenticatedKeyName" /> is the unspecified struct default.</exception>
    public static ClientAssertionAuthenticationResult Authenticated(SecretName authenticatedKeyName) =>
        authenticatedKeyName.IsSpecified
            ? new ClientAssertionAuthenticationResult(authenticatedKeyName, rejection: null)
            : throw new ArgumentException(
                "An authenticated result must name the key that verified the assertion.",
                nameof(authenticatedKeyName));

    /// <summary>Creates a refused result.</summary>
    /// <param name="rejection">Why the assertion was refused.</param>
    /// <returns>The refused result.</returns>
    public static ClientAssertionAuthenticationResult Rejected(ClientAssertionRejection rejection) =>
        new(authenticatedKeyName: null, rejection);
}
