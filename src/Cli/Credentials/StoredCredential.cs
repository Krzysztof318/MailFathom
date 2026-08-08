// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Credentials;

/// <summary>One deployment the operator has signed in to, under the name they gave it.</summary>
/// <param name="Endpoint">The address the profile reaches, so a command needs only the name.</param>
/// <param name="Token">The bearer credential, encrypted; see <see cref="TokenProtector" />.</param>
/// <param name="Credential">The name the deployment reported for the credential, kept so the command can say who it is signed in as without asking again.</param>
/// <param name="Session">What an OAuth sign-in left behind, absent from a profile holding an API key or a pasted token.</param>
/// <param name="KeyPair">Where a key-pair profile's private key lives, absent from every other kind of profile.</param>
/// <param name="Transport">What the operator accepted about this deployment's transport, absent from a profile that accepted nothing beyond the default.</param>
/// <remarks>
/// <para>
/// The ways of signing in leave different amounts behind, and the difference is two nullable members rather than three
/// kinds of profile. An API key is a credential that stays valid until the deployment stops accepting it, so there is
/// nothing to remember beside it; an OAuth sign-in issues a token that expires within the hour, so what has to be kept
/// is whatever renews it without the operator present.
/// </para>
/// <para>
/// A key-pair profile is the one that stores no credential at all. <see cref="Token" /> is sealed empty for it and every
/// command mints a fresh assertion from the key named here, which is why the two members are never both present: one
/// says how a stored credential is renewed and the other says that there is none to renew.
/// </para>
/// <para>
/// <see cref="Transport" /> is orthogonal to all three: it says what the connection carrying the credential is protected
/// by, whichever kind the credential is. It is absent from a profile signed in over an ordinary HTTPS connection, which
/// is why a store written before the member existed still reads.
/// </para>
/// </remarks>
internal sealed record StoredCredential(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("credential")] string Credential,
    [property: JsonPropertyName("session")] StoredOAuthSession? Session = null,
    [property: JsonPropertyName("keyPair")] StoredKeyPair? KeyPair = null,
    [property: JsonPropertyName("transport")] StoredTransportTrust? Transport = null)
{
    /// <inheritdoc />
    /// <remarks>Redacted, so no diagnostic or exception message prints the token by formatting the record it lives in — even encrypted, which is a value worth not scattering.</remarks>
    public override string ToString() => $"{nameof(StoredCredential)} {{ {this.Endpoint}, {this.Credential} }}";
}

/// <summary>Where a key-pair profile's private key lives, so every command can mint the credential it presents.</summary>
/// <param name="PrivateKeyPath">The absolute path of the operator's private key.</param>
/// <remarks>
/// A path rather than the key itself, deliberately and in both directions. The key stays wherever the operator generated
/// it, under whatever protection they gave that file, and the credential store gains nothing worth stealing by
/// remembering a profile; copying the key into the store would undo the property the method exists for at the one place
/// this command could have preserved it.
/// <para>
/// It is not a secret and is stored in clear, like the endpoint and the issuer beside it. What it names is.
/// </para>
/// </remarks>
internal sealed record StoredKeyPair(
    [property: JsonPropertyName("privateKeyPath")] string PrivateKeyPath);

/// <summary>What an operator accepted about one deployment's transport, beyond what is protected by default.</summary>
/// <param name="PinnedCertificateFingerprint">The SHA-256 fingerprint of the one certificate this profile accepts, or <see langword="null" /> to require a certificate this machine trusts on its own.</param>
/// <param name="AcceptsClearText">Whether the operator accepted that this profile's requests cross the network unprotected.</param>
/// <remarks>
/// <para>
/// Both members record a decision taken once, at <c>login</c>, about one deployment. Neither is a switch that turns a
/// protection off: a pin is stricter than the chain validation it replaces, because the profile then accepts exactly the
/// certificate the operator was shown and refuses every other, including one that would have validated on its own. The
/// clear-text member records that the operator was told the credential travels unprotected and said to continue anyway,
/// so no later command asks again and none of them widens into the other.
/// </para>
/// <para>
/// Neither is a secret and both are stored in clear, like the endpoint beside them. A fingerprint is a public value by
/// construction — it is what the deployment presents to anybody who connects — and what it protects is that this profile
/// keeps talking to the same deployment.
/// </para>
/// </remarks>
internal sealed record StoredTransportTrust(
    [property: JsonPropertyName("pinnedCertificateFingerprint")] string? PinnedCertificateFingerprint = null,
    [property: JsonPropertyName("clearText")] bool AcceptsClearText = false)
{
    /// <summary>Gets the trust a profile holds when it accepted nothing beyond what is protected by default.</summary>
    internal static StoredTransportTrust Protected { get; } = new();
}

/// <summary>What an OAuth sign-in has to remember so the next command does not ask the operator to sign in again.</summary>
/// <param name="RefreshToken">The credential a spent access token is exchanged for a new one with, encrypted; see <see cref="TokenProtector" />.</param>
/// <param name="AccessTokenExpiresAt">When the stored access token stops being accepted, which is what decides whether a renewal happens at all.</param>
/// <param name="TokenEndpoint">Where the exchange is made, taken from the authorization server's own discovery document at sign-in.</param>
/// <param name="Issuer">Which authorization server the session belongs to, so a message can name it and a re-sign-in reaches the same one.</param>
/// <param name="ClientId">The client this session was issued to, which a renewal has to present again.</param>
/// <param name="Resource">The resource identifier the renewed token's audience must name.</param>
/// <param name="Scope">The scopes the renewal asks for, so a renewed token is not narrower than the one it replaces.</param>
/// <remarks>
/// <para>
/// The refresh token is the longer-lived of the two secrets and is sealed with the same envelope as the access token,
/// under the same endpoint binding. Anything weaker would be a regression in the one value worth protecting most.
/// </para>
/// <para>
/// The endpoint, issuer, client, resource, and scope are not secrets and are stored in clear. They are recorded rather
/// than rediscovered because a renewal happens on an ordinary command that a person is waiting on, and re-reading two
/// discovery documents to spend a refresh token would put two more round trips in front of every expired session. A
/// deployment that moves one of them is answered by signing in again, which is what re-reads them.
/// </para>
/// </remarks>
internal sealed record StoredOAuthSession(
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("accessTokenExpiresAt")] DateTimeOffset AccessTokenExpiresAt,
    [property: JsonPropertyName("tokenEndpoint")] string TokenEndpoint,
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("scope")] string Scope)
{
    /// <inheritdoc />
    /// <remarks>Redacted, for the reason the profile it hangs from is: the refresh token is the credential that outlives everything else here.</remarks>
    public override string ToString() => $"{nameof(StoredOAuthSession)} {{ {this.Issuer}, expires {this.AccessTokenExpiresAt:u} }}";
}

/// <summary>Everything the command remembers between invocations.</summary>
/// <param name="Default">The profile a command uses when the operator names none, which <c>login</c> and <c>switch</c> both set.</param>
/// <param name="Profiles">The signed-in deployments, keyed by the operator's own name for each.</param>
/// <remarks>
/// Keyed by name rather than by address, because a name is what an operator types and an address is what changes: a
/// deployment that moves port or gains a domain keeps its name, and its profile follows rather than becoming a second
/// entry nobody meant to create.
/// </remarks>
internal sealed record StoredCredentials(
    [property: JsonPropertyName("default")] string? Default,
    [property: JsonPropertyName("profiles")] Dictionary<string, StoredCredential> Profiles)
{
    /// <summary>Builds the state a machine that has never signed in is in.</summary>
    /// <returns>An empty store.</returns>
    internal static StoredCredentials Empty() =>
        new(Default: null, new Dictionary<string, StoredCredential>(StringComparer.OrdinalIgnoreCase));
}
