// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;
using MailFathom.Common.ClientAssertions;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Judges the assertion a request presented against the public keys this deployment holds for its owners.</summary>
/// <remarks>
/// <para>
/// One indexed read and one signature check, which is the whole difference between this and the configured verification
/// beside it. An assertion names the key that signed it in its own <c>kid</c> header, and that name is the key's own
/// fingerprint, so the credential row is resolved before any material is imported — where the configured path imports
/// every registered key on every request and tries them in turn.
/// </para>
/// <para>
/// The fingerprint is read unverified and decides nothing but which key the signature is checked against. An assertion
/// naming a key registered for another owner is refused by that key's own signature check rather than admitted as that
/// owner, and one naming nothing this deployment holds is refused for the same reason a key nobody holds is.
/// </para>
/// <para>
/// The key is imported per request and released with the request, so a public key an administrator replaced reaches the
/// next assertion with no cache to invalidate. What that costs is one PEM import per authenticated request, which is
/// bounded work an already-resolved credential provoked.
/// </para>
/// <para>
/// Every refusal is one outcome. Nothing this produces carries the presented assertion or the key it named; what a
/// record names is the credential's identifier, which is MailFathom's own name for the row.
/// </para>
/// <para>
/// Four of the refusals are recorded, and every one of them is read after the signature has verified: a credential an
/// administrator disabled, an assertion claiming a longer life than the surface accepts, one carrying no usable replay
/// identifier, and one whose identifier had already been served. Each names an operator's own act or a client of theirs
/// misbehaving, which is what a log line is for. Nothing before the signature is recorded, for the reason the
/// configured verification beside this one records nothing there either: a fingerprint travels in the clear in every
/// assertion its client ever sent, so a line written where one merely resolves a row would let whoever captured one
/// fill the deployment's log by sending unsigned tokens.
/// </para>
/// </remarks>
internal sealed partial class OwnerClientAssertionAuthenticator
{
    private readonly IOwnerCredentialStore credentials;
    private readonly ClientAssertionReplayStore replayStore;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<OwnerClientAssertionAuthenticator> logger;

    /// <summary>Initializes a new owner client assertion authenticator.</summary>
    /// <param name="credentials">Where the owners' credentials are kept.</param>
    /// <param name="replayStore">Where an assertion's identifier is spent, so none is served twice.</param>
    /// <param name="timeProvider">The clock an assertion's permitted window is judged against.</param>
    /// <param name="logger">Where a refusal against a credential this deployment holds is recorded.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnerClientAssertionAuthenticator(
        IOwnerCredentialStore credentials,
        ClientAssertionReplayStore replayStore,
        TimeProvider timeProvider,
        ILogger<OwnerClientAssertionAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(replayStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.credentials = credentials;
        this.replayStore = replayStore;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Judges the assertion an <c>Authorization</c> header carried.</summary>
    /// <param name="audience">The audience the surface publishes, which the assertion must name.</param>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="cancellationToken">Cancels the credential read.</param>
    /// <returns>What the request was admitted as, or the reason the assertion was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="audience" /> is <see langword="null" />.</exception>
    public async Task<OwnerClientAssertionAuthenticationResult> AuthenticateAsync(
        string audience,
        string? authorizationHeaderValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audience);

        if (!BearerCredentialHeader.TryRead(authorizationHeaderValue, out var presentedAssertion))
        {
            return OwnerClientAssertionAuthenticationResult.Rejected(
                string.IsNullOrWhiteSpace(authorizationHeaderValue)
                    ? ClientAssertionRejection.CredentialMissing
                    : ClientAssertionRejection.CredentialMalformed);
        }

        // The declared type and the key identifier are both read before any credential is looked up, so a request
        // presenting something else costs the host nothing beyond parsing what it sent.
        if (!UnverifiedJsonWebToken.TryReadDeclaredType(presentedAssertion, out var declaredType)
            || !string.Equals(declaredType, ClientAssertion.DeclaredType, StringComparison.Ordinal))
        {
            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.NotAnAssertion);
        }

        if (!UnverifiedJsonWebToken.TryReadKeyId(presentedAssertion, out var keyId)
            || !OwnerCredentialLookup.TryCreate(keyId, out var fingerprint))
        {
            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.SignatureUnrecognized);
        }

        var credential = await this.credentials.FindAsync(
            OwnerCredentialMethod.PublicKey,
            fingerprint,
            cancellationToken);

        // A disabled credential is carried into verification rather than refused here, so that the refusal is recorded
        // against a caller who has proven it holds the private key. Nothing else about it is admitted: the enablement
        // is judged below, and it produces the same refusal this line would have.
        if (credential is not { Material: { } material })
        {
            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.SignatureUnrecognized);
        }

        using var key = ClientAssertionKeyMaterial.ReadPublicKey(material, out _);

        if (key is null)
        {
            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.SignatureUnrecognized);
        }

        return await this.VerifyAsync(audience, presentedAssertion, fingerprint, key, credential);
    }

    /// <summary>Verifies the assertion against the resolved key and then judges what it claims.</summary>
    /// <remarks>The order is what keeps every later rule reading a signed value: nothing about the credential's enablement, the audience, the expiry, or the identifier is acted on until the signature has proven whose assertion this is.</remarks>
    private async Task<OwnerClientAssertionAuthenticationResult> VerifyAsync(
        string audience,
        string presentedAssertion,
        OwnerCredentialLookup fingerprint,
        AsymmetricAlgorithm key,
        ResolvedOwnerCredential credential)
    {
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            presentedAssertion,
            ClientAssertionValidation.ParametersFor(audience, [SecurityKeyOver(key)], this.timeProvider));

        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken assertion)
        {
            return OwnerClientAssertionAuthenticationResult.Rejected(
                ClientAssertionRejection.SignatureUnrecognized);
        }

        // Read after the signature and not before it: a fingerprint travels in the clear in every assertion the client
        // ever sent, so refusing a disabled credential where it is resolved would let whoever captured one write a line
        // per request. Verification is what makes the caller the client this credential belongs to.
        if (!credential.Enabled)
        {
            this.LogDisabledCredentialPresented(credential.Id);

            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.SignatureUnrecognized);
        }

        var expiresAt = new DateTimeOffset(assertion.ValidTo, TimeSpan.Zero);

        if (expiresAt > this.timeProvider.GetUtcNow() + ClientAssertionValidation.FurthestPermittedExpiry)
        {
            this.LogOverlongAssertionPresented(credential.Id);

            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.ClaimsUnacceptable);
        }

        if (assertion.Id is not { Length: > 0 } identifier || identifier.Length > ClientAssertion.IdentifierLengthLimit)
        {
            this.LogUnusableIdentifierPresented(credential.Id);

            return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.ClaimsUnacceptable);
        }

        if (this.replayStore.TrySpend(fingerprint.Value, identifier, expiresAt))
        {
            return OwnerClientAssertionAuthenticationResult.Authenticated(AdmittedOwnerCredential.For(credential));
        }

        this.LogReplayedAssertionPresented(credential.Id);

        return OwnerClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.IdentifierAlreadySpent);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented a verified assertion signed by the key of credential {CredentialId}, which "
            + "this deployment holds and does not accept. The request was refused with the same response as any other "
            + "refusal; enable the credential, or provision the client a new one.")]
    private partial void LogDisabledCredentialPresented(Guid credentialId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented an assertion for credential {CredentialId} that claims a longer life than this "
            + "endpoint accepts. The request was refused; the client is minting assertions with too distant an expiry.")]
    private partial void LogOverlongAssertionPresented(Guid credentialId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented an assertion for credential {CredentialId} carrying no replay identifier this "
            + "endpoint can spend. The request was refused; the client is minting assertions without a usable "
            + "identifier.")]
    private partial void LogUnusableIdentifierPresented(Guid credentialId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented an assertion for credential {CredentialId} whose identifier this process has "
            + "already served. The request was refused; either the client reused an identifier or an assertion was "
            + "captured and replayed.")]
    private partial void LogReplayedAssertionPresented(Guid credentialId);

    /// <summary>Wraps the resolved public key as something the validator can verify against.</summary>
    /// <remarks>
    /// The signature provider cache is turned off for the same reason the configured path turns it off: the key is
    /// imported per request and released with it, so a cached provider would outlive the key it holds and every later
    /// request would be refused for a signature nothing was wrong with.
    /// </remarks>
    private static SecurityKey SecurityKeyOver(AsymmetricAlgorithm key)
    {
        SecurityKey securityKey = key switch
        {
            RSA rsa => new RsaSecurityKey(rsa),
            ECDsa ecdsa => new ECDsaSecurityKey(ecdsa),
            _ => throw new NotSupportedException("The key is of a kind no permitted signature algorithm covers."),
        };

        securityKey.CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false };

        return securityKey;
    }
}

/// <summary>The outcome of judging one presented assertion against the public keys an owner registered.</summary>
/// <remarks>
/// A refused credential is an expected outcome of serving an open endpoint rather than an exceptional state, so
/// authentication returns this instead of throwing. The successful result carries what every owner-facing method
/// establishes: the credential that matched, the owner the request acts for, and what that request may do.
/// </remarks>
internal sealed record OwnerClientAssertionAuthenticationResult
{
    private OwnerClientAssertionAuthenticationResult(
        AdmittedOwnerCredential? admitted,
        ClientAssertionRejection? rejection)
    {
        this.Admitted = admitted;
        this.Rejection = rejection;
    }

    /// <summary>Gets what the request was admitted as, or <see langword="null" /> when the assertion was refused.</summary>
    public AdmittedOwnerCredential? Admitted { get; }

    /// <summary>Gets why the assertion was refused, or <see langword="null" /> when it authenticated.</summary>
    /// <remarks>Nothing serves it to a caller: every value produces one indistinguishable response, and what a refusal an operator can act on records is written where the refusal was decided rather than carried out to whoever asks.</remarks>
    public ClientAssertionRejection? Rejection { get; }

    /// <summary>Creates a successful result naming what the request was admitted as.</summary>
    /// <param name="admitted">The credential that matched, the owner it names, and what it grants.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admitted" /> is <see langword="null" />.</exception>
    public static OwnerClientAssertionAuthenticationResult Authenticated(AdmittedOwnerCredential admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);

        return new OwnerClientAssertionAuthenticationResult(admitted, rejection: null);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="rejection">Why the assertion was refused.</param>
    /// <returns>The refused result.</returns>
    public static OwnerClientAssertionAuthenticationResult Rejected(ClientAssertionRejection rejection) =>
        new(admitted: null, rejection);
}
