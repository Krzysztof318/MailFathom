// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Common.ClientAssertions;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Judges the assertion a request presented against the client public keys a deployment configured.</summary>
/// <remarks>
/// <para>
/// The keys are resolved per request rather than cached, which is what the secret machinery promises everywhere else:
/// material rotated behind an unchanged reference reaches the next request with no cache to invalidate and no restart to
/// schedule. What that costs here is one PEM import per configured key per request, which is a handful of keys and a
/// bounded amount of work an authenticated caller provokes.
/// </para>
/// <para>
/// Verification stops at the key that works, unlike the API key comparison beside it, and the difference is what the two
/// are comparing. A key comparison that stopped early would leak where in the list a secret sits; a signature check
/// compares against public material, so which key verified an assertion is not a fact anyone has to be kept from
/// learning — and the client already knows, having chosen the key it signed with.
/// </para>
/// <para>
/// A key whose configured lifetime has ended still takes part in verification and is refused afterwards. Excluding it
/// beforehand would be indistinguishable from a signature nobody's key made, and an operator whose scheduled job has
/// silently stopped working needs the log line that names the retired key rather than one more unrecognized credential.
/// </para>
/// <para>
/// Nothing this produces or logs carries the presented assertion, a configured reference, or key material. The name of a
/// configured key appears in the log, as it does for an API key, because it is MailFathom's own name for a credential
/// rather than any part of one.
/// </para>
/// </remarks>
internal sealed partial class ClientAssertionAuthenticator
{
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly ClientAssertionReplayStore replayStore;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ClientAssertionAuthenticator> logger;

    /// <summary>Initializes a new client assertion authenticator.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into public key material.</param>
    /// <param name="replayStore">Where an assertion's identifier is spent, so none is served twice.</param>
    /// <param name="timeProvider">The clock a key's lifetime and an assertion's permitted window are judged against.</param>
    /// <param name="logger">The log a refusal and a configuration fault are recorded in.</param>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    public ClientAssertionAuthenticator(
        ISecretReferenceResolver secretReferenceResolver,
        ClientAssertionReplayStore replayStore,
        TimeProvider timeProvider,
        ILogger<ClientAssertionAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(replayStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.secretReferenceResolver = secretReferenceResolver;
        this.replayStore = replayStore;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Judges the assertion an <c>Authorization</c> header carried.</summary>
    /// <param name="configuredKeys">The client public keys the surface configured, in configuration order.</param>
    /// <param name="audience">The audience the surface publishes, which the assertion must name.</param>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="cancellationToken">Cancels the retrieval of the configured key material.</param>
    /// <returns>The name of the key that verified the assertion, or the reason it was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuredKeys" /> or <paramref name="audience" /> is <see langword="null" />.</exception>
    public async Task<ClientAssertionAuthenticationResult> AuthenticateAsync(
        IReadOnlyList<ConfiguredSecret> configuredKeys,
        string audience,
        string? authorizationHeaderValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredKeys);
        ArgumentNullException.ThrowIfNull(audience);

        if (!BearerCredentialHeader.TryRead(authorizationHeaderValue, out var presentedAssertion))
        {
            return ClientAssertionAuthenticationResult.Rejected(string.IsNullOrWhiteSpace(authorizationHeaderValue)
                ? ClientAssertionRejection.CredentialMissing
                : ClientAssertionRejection.CredentialMalformed);
        }

        // The declared type is read before any key material is resolved, so a request presenting something else costs
        // the host nothing beyond parsing what it sent. The validator checks it again against the same constant, which
        // is what makes this a filter rather than the decision.
        if (!UnverifiedJsonWebToken.TryReadDeclaredType(presentedAssertion, out var declaredType)
            || !string.Equals(declaredType, ClientAssertion.DeclaredType, StringComparison.Ordinal))
        {
            return ClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.NotAnAssertion);
        }

        var verificationKeys = await this.ReadVerificationKeysAsync(configuredKeys, cancellationToken);

        try
        {
            return await this.VerifyAsync(configuredKeys, audience, presentedAssertion, verificationKeys);
        }
        finally
        {
            foreach (var verificationKey in verificationKeys)
            {
                verificationKey.Algorithm.Dispose();
            }
        }
    }

    /// <summary>Verifies the assertion and then judges what it claims.</summary>
    /// <remarks>The order is what keeps every later rule reading a signed value: nothing about the audience, the expiry, or the identifier is acted on until the signature has proven whose assertion this is.</remarks>
    private async Task<ClientAssertionAuthenticationResult> VerifyAsync(
        IReadOnlyList<ConfiguredSecret> configuredKeys,
        string audience,
        string presentedAssertion,
        IReadOnlyList<VerificationKey> verificationKeys)
    {
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            presentedAssertion,
            ClientAssertionValidation.ParametersFor(
                audience,
                [.. verificationKeys.Select(key => key.SecurityKey)],
                this.timeProvider));

        if (!validation.IsValid)
        {
            return ClientAssertionAuthenticationResult.Rejected(RejectionFor(validation.Exception));
        }

        if (validation.SecurityToken is not JsonWebToken assertion
            || assertion.SigningKey?.KeyId is not { } verifyingKeyId
            || verificationKeys.FirstOrDefault(key =>
                string.Equals(key.Name.Value, verifyingKeyId, StringComparison.Ordinal)) is not { } verifyingKey)
        {
            return ClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.SignatureUnrecognized);
        }

        var now = this.timeProvider.GetUtcNow();

        if (this.HasRetiredKey(configuredKeys, verifyingKey.Name, now))
        {
            return ClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.SignatureUnrecognized);
        }

        var expiresAt = new DateTimeOffset(assertion.ValidTo, TimeSpan.Zero);

        if (expiresAt > now + ClientAssertionValidation.FurthestPermittedExpiry)
        {
            this.LogOverlongAssertionPresented(verifyingKey.Name.Value!);

            return ClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.ClaimsUnacceptable);
        }

        if (assertion.Id is not { Length: > 0 } identifier || identifier.Length > ClientAssertion.IdentifierLengthLimit)
        {
            return ClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.ClaimsUnacceptable);
        }

        if (!this.replayStore.TrySpend(verifyingKey.Name, identifier, expiresAt))
        {
            this.LogReplayedAssertionPresented(verifyingKey.Name.Value!);

            return ClientAssertionAuthenticationResult.Rejected(ClientAssertionRejection.IdentifierAlreadySpent);
        }

        return ClientAssertionAuthenticationResult.Authenticated(verifyingKey.Name);
    }

    /// <summary>Reports whether the key that verified an assertion is one the deployment no longer accepts.</summary>
    /// <remarks>A lifetime that no longer parses is treated as ended rather than as unbounded, so a deployment edited into an unreadable state closes the endpoint instead of opening it.</remarks>
    private bool HasRetiredKey(IReadOnlyList<ConfiguredSecret> configuredKeys, SecretName keyName, DateTimeOffset now)
    {
        var configuredKey = configuredKeys.FirstOrDefault(
            candidate => string.Equals(candidate.Name, keyName.Value, StringComparison.Ordinal));

        if (configuredKey is null
            || !SecretLifetime.TryParse(configuredKey.Lifetime, out var lifetime)
            || lifetime.HasExpiredAt(now))
        {
            this.LogRetiredKeyPresented(keyName.Value!);

            return true;
        }

        return false;
    }

    /// <summary>Resolves and parses every configured public key, skipping the ones no request could be verified against.</summary>
    /// <remarks>
    /// Startup already proved every reference resolves and every piece of material parses, so a fault here means the
    /// deployment changed underneath a running process — which an operator has to see, and which must not take the other
    /// configured clients down with it.
    /// </remarks>
    private async Task<IReadOnlyList<VerificationKey>> ReadVerificationKeysAsync(
        IReadOnlyList<ConfiguredSecret> configuredKeys,
        CancellationToken cancellationToken)
    {
        var verificationKeys = new List<VerificationKey>(configuredKeys.Count);

        // The loop stays because each step awaits a retrieval and each key it produces is owned by the caller.
        foreach (var configuredKey in configuredKeys)
        {
            if (!SecretName.TryCreate(configuredKey.Name, out var keyName))
            {
                this.LogKeyDeclarationUnusable();

                continue;
            }

            var resolution = await this.secretReferenceResolver.ResolveAsync(
                configuredKey.SecretReference,
                cancellationToken);

            if (resolution.Secret is not { } material)
            {
                this.LogKeyMaterialUnavailable(keyName.Value!, resolution.Failure);

                continue;
            }

            using (material)
            {
                if (ReadPublicKey(material) is { } publicKey)
                {
                    verificationKeys.Add(VerificationKey.Over(keyName, publicKey));
                }
                else
                {
                    this.LogKeyMaterialUnusable(keyName.Value!);
                }
            }
        }

        return verificationKeys;
    }

    /// <summary>Reads one configured key's material as a public key.</summary>
    /// <remarks>The text view rather than the raw bytes, for the reason an API key is compared on one: PEM provisioned as a file, a Compose secret, or a mounted Kubernetes value routinely carries a trailing newline the operator never sees.</remarks>
    private static AsymmetricAlgorithm? ReadPublicKey(ResolvedSecret material)
    {
        var revealedText = GC.AllocateArray<char>(material.TextLength, pinned: true);

        try
        {
            material.RevealTextInto(revealedText);

            return ClientAssertionKeyMaterial.ReadPublicKey(revealedText, out _);
        }
        finally
        {
            revealedText.AsSpan().Clear();
        }
    }

    /// <summary>Names why the validator refused an assertion, in the vocabulary the log keeps.</summary>
    /// <remarks>Only the signature is told apart from everything else, because that is the one distinction an operator acts on differently: a signature nobody's key made is a client presenting the wrong key, and every other refusal is a client minting the wrong document.</remarks>
    private static ClientAssertionRejection RejectionFor(Exception? refusal) =>
        refusal is SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException
            ? ClientAssertionRejection.SignatureUnrecognized
            : ClientAssertionRejection.ClaimsUnacceptable;

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A configured client public key carries no usable name, so it cannot verify an assertion. Startup "
            + "validates this, which means the configuration changed underneath the running process.")]
    private partial void LogKeyDeclarationUnusable();

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The material behind client public key {PublicKeyName} could not be retrieved, so that key cannot "
            + "verify an assertion [{Failure}].")]
    private partial void LogKeyMaterialUnavailable(string publicKeyName, SecretResolutionFailure? failure);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The material behind client public key {PublicKeyName} is no longer a usable public key, so that key "
            + "cannot verify an assertion. Startup validates this, which means the material changed underneath the "
            + "running process.")]
    private partial void LogKeyMaterialUnusable(string publicKeyName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented an assertion signed by client public key {PublicKeyName}, whose configured "
            + "lifetime has ended. The request was refused with the same response as any other refusal; register the "
            + "client's new public key or extend the lifetime.")]
    private partial void LogRetiredKeyPresented(string publicKeyName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented an assertion signed by client public key {PublicKeyName} that claims a longer "
            + "life than this endpoint accepts. The request was refused; the client is minting assertions with too "
            + "distant an expiry.")]
    private partial void LogOverlongAssertionPresented(string publicKeyName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented an assertion signed by client public key {PublicKeyName} whose identifier this "
            + "process has already served. The request was refused; either the client reused an identifier or an "
            + "assertion was captured and replayed.")]
    private partial void LogReplayedAssertionPresented(string publicKeyName);

    /// <summary>One configured public key, ready to verify with, paired with the name it is known by.</summary>
    /// <remarks>
    /// The name travels as the key's identifier, which is how the validator's answer says which configured key verified
    /// the assertion. It is never compared against anything the credential carries: the resolver hands over every key
    /// whatever the assertion said, so the identifier is an output of verification rather than an input to it.
    /// <para>
    /// The algorithm is kept beside the security key because the caller owns and disposes it, and the security key holds
    /// it without owning it.
    /// </para>
    /// </remarks>
    private sealed record VerificationKey(SecretName Name, AsymmetricAlgorithm Algorithm, SecurityKey SecurityKey)
    {
        /// <summary>Wraps one parsed public key as something the validator can verify against.</summary>
        /// <param name="name">The configured name of the key.</param>
        /// <param name="algorithm">The parsed key, whose ownership passes to the returned value's caller.</param>
        /// <returns>The pairing.</returns>
        /// <exception cref="NotSupportedException">Thrown when the key is of a kind no permitted signature algorithm covers, which the material reader already refuses.</exception>
        internal static VerificationKey Over(SecretName name, AsymmetricAlgorithm algorithm)
        {
            SecurityKey securityKey = algorithm switch
            {
                RSA rsa => new RsaSecurityKey(rsa),
                ECDsa ecdsa => new ECDsaSecurityKey(ecdsa),
                _ => throw new NotSupportedException("The key is of a kind no permitted signature algorithm covers."),
            };

            securityKey.KeyId = name.Value;

            // The library otherwise keeps the signature provider it builds for a key in a process-wide cache, keyed by
            // the key material. This key is parsed per request and released with it, so a cached provider would outlive
            // the key it holds and every later request would be refused for a signature nothing was wrong with. Opting
            // one key out is what keeps per-request resolution — and with it rotation without a restart — possible.
            securityKey.CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false };

            return new VerificationKey(name, algorithm, securityKey);
        }
    }
}
