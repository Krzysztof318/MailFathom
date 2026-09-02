// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Security.OAuth;

namespace MailFathom.Infrastructure.Security.ApiKeys;

/// <summary>Judges the key a request presented against the credentials this deployment holds for its owners.</summary>
/// <remarks>
/// <para>
/// One indexed read and no comparison loop, which is the whole difference between this and the configured-key
/// comparison beside it. A key is reduced to its digest and the digest resolves at most one row, so what a deployment
/// spends on a request is independent of how many credentials it holds and of where in them the presented key sits —
/// there is no list, so there is no position to leak.
/// </para>
/// <para>
/// <strong>One failure path.</strong> A request carrying no credential, one that could never have been minted here,
/// one whose digest resolves nothing, and one resolving a credential somebody disabled are all refused as
/// <see cref="ApiKeyRejection.CredentialUnrecognized" /> where they are distinguishable at all, and every one of them
/// produces the same response. What the vocabulary is for is the server log, exactly as it is for the configured keys.
/// </para>
/// <para>
/// Nothing written down is the credential. Neither the returned result nor anything logged on the way to it carries
/// the presented key or its digest; what a record names is the credential's identifier, which is MailFathom's own name
/// for the row rather than any part of what was presented.
/// </para>
/// </remarks>
public sealed class OwnerApiKeyAuthenticator
{
    private readonly IOwnerCredentialStore credentials;
    private readonly IOwnerApiKeyMinter minter;

    /// <summary>Initializes a new owner API key authenticator.</summary>
    /// <param name="credentials">Where the owners' credentials are kept.</param>
    /// <param name="minter">What reduces a presented key to the value a credential is resolved by.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public OwnerApiKeyAuthenticator(IOwnerCredentialStore credentials, IOwnerApiKeyMinter minter)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(minter);

        this.credentials = credentials;
        this.minter = minter;
    }

    /// <summary>Judges the credential an <c>Authorization</c> header carried.</summary>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="cancellationToken">Cancels the credential read.</param>
    /// <returns>What the request was admitted as, or the reason the credential was refused.</returns>
    public async Task<OwnerApiKeyAuthenticationResult> AuthenticateAsync(
        string? authorizationHeaderValue,
        CancellationToken cancellationToken)
    {
        if (!BearerCredentialHeader.TryRead(authorizationHeaderValue, out var presentedKey))
        {
            return OwnerApiKeyAuthenticationResult.Rejected(string.IsNullOrWhiteSpace(authorizationHeaderValue)
                ? ApiKeyRejection.CredentialMissing
                : ApiKeyRejection.CredentialMalformed);
        }

        if (!this.minter.TryDigest(presentedKey, out var lookup))
        {
            return OwnerApiKeyAuthenticationResult.Rejected(ApiKeyRejection.CredentialMalformed);
        }

        var credential = await this.credentials.FindAsync(
            OwnerCredentialMethod.ApiKey,
            lookup,
            cancellationToken);

        return credential is { Enabled: true }
            ? OwnerApiKeyAuthenticationResult.Authenticated(AdmittedOwnerCredential.For(credential))
            : OwnerApiKeyAuthenticationResult.Rejected(ApiKeyRejection.CredentialUnrecognized);
    }
}

/// <summary>The outcome of judging one presented key against the credentials an owner holds.</summary>
/// <remarks>
/// A refused credential is an expected outcome of serving an open endpoint rather than an exceptional state, so
/// authentication returns this instead of throwing. The successful result carries what every owner-facing method
/// establishes: the credential that matched, the owner the request acts for, and what that request may do.
/// </remarks>
public sealed record OwnerApiKeyAuthenticationResult
{
    private OwnerApiKeyAuthenticationResult(AdmittedOwnerCredential? admitted, ApiKeyRejection? rejection)
    {
        this.Admitted = admitted;
        this.Rejection = rejection;
    }

    /// <summary>Gets whether the presented credential authenticated.</summary>
    public bool Succeeded => this.Admitted is not null;

    /// <summary>Gets what the request was admitted as, or <see langword="null" /> when the credential was refused.</summary>
    public AdmittedOwnerCredential? Admitted { get; }

    /// <summary>Gets why the credential was refused, or <see langword="null" /> when it authenticated.</summary>
    /// <remarks>It reaches the server log only. Every value produces one indistinguishable response.</remarks>
    public ApiKeyRejection? Rejection { get; }

    /// <summary>Creates a successful result naming what the request was admitted as.</summary>
    /// <param name="admitted">The credential that matched, the owner it names, and what it grants.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admitted" /> is <see langword="null" />.</exception>
    public static OwnerApiKeyAuthenticationResult Authenticated(AdmittedOwnerCredential admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);

        return new OwnerApiKeyAuthenticationResult(admitted, rejection: null);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="rejection">Why the credential was refused.</param>
    /// <returns>The refused result.</returns>
    public static OwnerApiKeyAuthenticationResult Rejected(ApiKeyRejection rejection) =>
        new(admitted: null, rejection);
}
