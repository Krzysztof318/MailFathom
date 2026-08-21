// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Credentials;

/// <summary>A profile a command is about to act through, with its tokens readable.</summary>
/// <param name="Name">The operator's name for the deployment, which is what a message says rather than an address.</param>
/// <param name="Endpoint">The address to send to.</param>
/// <param name="Token">The bearer credential, opened.</param>
/// <param name="Credential">The name the deployment reported for the credential when it was stored.</param>
/// <param name="Session">The OAuth session this profile holds, or <see langword="null" /> when the credential is an API key, a pasted token, or a key pair.</param>
/// <param name="KeyPair">Where this profile's private key lives, or <see langword="null" /> when it holds a stored credential instead.</param>
/// <remarks>
/// Distinct from <see cref="StoredCredential" /> because the two hold the tokens in different states: what is written to
/// the file is sealed, and what a command sends is not. Keeping one type for both would leave every caller having to
/// know which of the two it was holding.
/// <para>
/// <see cref="Token" /> is empty as this leaves the store when <see cref="KeyPair" /> is present, because such a profile
/// stores no credential. <see cref="Administration.DeploymentAccess" /> is what fills it in, on the same seam an expired
/// access token is renewed on, so nothing above it has to know which kind of profile it holds.
/// </para>
/// </remarks>
internal sealed record SignedInProfile(
    string Name,
    Uri Endpoint,
    string Token,
    string Credential,
    OAuthSession? Session = null,
    StoredKeyPair? KeyPair = null)
{
    /// <summary>Gets what this profile's connection to the deployment may accept.</summary>
    /// <remarks>
    /// Never absent as a command reads it, unlike the stored member it comes from: a profile written before the member
    /// existed, and one signed in over an ordinary HTTPS connection, both hold the default rather than nothing, so no
    /// call site has to decide what an absent value would mean.
    /// </remarks>
    internal StoredTransportTrust Trust { get; init; } = StoredTransportTrust.Protected;

    /// <inheritdoc />
    /// <remarks>Redacted, so no diagnostic or exception message prints the token by formatting the record it lives in.</remarks>
    public override string ToString() => $"{nameof(SignedInProfile)} {{ {this.Name}, {this.Endpoint}, {this.Credential} }}";
}

/// <summary>An OAuth sign-in a command can act on, with its refresh token readable.</summary>
/// <param name="RefreshToken">The credential a spent access token is exchanged for a new one with, opened.</param>
/// <param name="AccessTokenExpiresAt">When the access token stops being accepted.</param>
/// <param name="TokenEndpoint">Where the exchange is made.</param>
/// <param name="Issuer">The authorization server this session belongs to.</param>
/// <param name="ClientId">The client the session was issued to.</param>
/// <param name="Resource">The resource identifier a renewed token's audience must name.</param>
/// <param name="Scope">The scopes a renewal asks for.</param>
internal sealed record OAuthSession(
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    Uri TokenEndpoint,
    string Issuer,
    string ClientId,
    string Resource,
    string Scope)
{
    /// <inheritdoc />
    /// <remarks>Redacted, because the refresh token is the credential that outlives every other value here.</remarks>
    public override string ToString() => $"{nameof(OAuthSession)} {{ {this.Issuer}, expires {this.AccessTokenExpiresAt:u} }}";
}
