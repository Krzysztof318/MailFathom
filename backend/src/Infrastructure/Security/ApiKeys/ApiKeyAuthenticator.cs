// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Security.ApiKeys;

/// <summary>Judges the credential a request presented against the API keys a deployment configured for its own administrative surface.</summary>
/// <remarks>
/// <para>
/// The keys are resolved per request rather than cached, which is what the secret machinery already promises
/// everywhere else: material rotated behind an unchanged reference reaches the next operation with no cache to
/// invalidate and no restart to schedule. The schemes that ship today read a local file or an environment variable, and
/// a future network-backed store caches inside its own adapter, so the cost stays where the policy for it lives.
/// </para>
/// <para>
/// Comparison never touches the material directly. Each side is reduced to an HMAC-SHA-256 digest under a key this
/// process generates at construction and never publishes, and the digests are compared with
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})" />. Hashing first is
/// what keeps the length of a presented credential from leaking: a fixed-time comparison is only fixed-time over equal
/// lengths, and comparing raw material would answer "how long is the real key" to anyone willing to time it. The digest
/// key is per process rather than configured, because nothing outside this process ever needs to reproduce a digest.
/// </para>
/// <para>
/// Every configured key is evaluated, including one already matched and one already expired. Stopping early would make
/// the time a refusal takes depend on where in the list a key sits, and would make an expired key answer faster than an
/// unrecognized one — which is exactly the distinction the single generic refusal exists to hide.
/// </para>
/// </remarks>
public sealed partial class ApiKeyAuthenticator
{
    private const int DigestLength = 32;

    private readonly byte[] comparisonKey = RandomNumberGenerator.GetBytes(DigestLength);
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ApiKeyAuthenticator> logger;

    /// <summary>Initializes a new API key authenticator.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into key material.</param>
    /// <param name="timeProvider">The clock a bounded lifetime is judged against.</param>
    /// <param name="logger">The log a refusal and a configuration fault are recorded in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretReferenceResolver" /> or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public ApiKeyAuthenticator(
        ISecretReferenceResolver secretReferenceResolver,
        TimeProvider timeProvider,
        ILogger<ApiKeyAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.secretReferenceResolver = secretReferenceResolver;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Judges the credential an <c>Authorization</c> header carried.</summary>
    /// <param name="configuredKeys">The API keys the deployment configured, in configuration order.</param>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="cancellationToken">Cancels the retrieval of the configured key material.</param>
    /// <returns>The name of the key that matched, or the reason the credential was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuredKeys" /> is <see langword="null" />.</exception>
    /// <remarks>Neither the returned result nor anything logged on the way to it carries the presented credential, a configured reference, or key material.</remarks>
    public async Task<ApiKeyAuthenticationResult> AuthenticateAsync(
        IReadOnlyList<ConfiguredSecret> configuredKeys,
        string? authorizationHeaderValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredKeys);

        if (!BearerCredentialHeader.TryRead(authorizationHeaderValue, out var presentedCredential))
        {
            return ApiKeyAuthenticationResult.Rejected(string.IsNullOrWhiteSpace(authorizationHeaderValue)
                ? ApiKeyRejection.CredentialMissing
                : ApiKeyRejection.CredentialMalformed);
        }

        var presentedDigest = this.DigestOfCharacters(presentedCredential);
        var now = this.timeProvider.GetUtcNow();

        SecretName? authenticatedKeyName = null;
        var matchedExpiredKey = false;

        foreach (var configuredKey in configuredKeys)
        {
            var match = await this.MatchAsync(configuredKey, presentedDigest, now, cancellationToken);

            authenticatedKeyName = match.AuthenticatedKeyName ?? authenticatedKeyName;
            matchedExpiredKey |= match.Rejection == ApiKeyRejection.CredentialExpired;
        }

        if (authenticatedKeyName is { } keyName)
        {
            return ApiKeyAuthenticationResult.Authenticated(keyName);
        }

        return ApiKeyAuthenticationResult.Rejected(matchedExpiredKey
            ? ApiKeyRejection.CredentialExpired
            : ApiKeyRejection.CredentialUnrecognized);
    }

    /// <summary>Judges one configured key against the presented digest.</summary>
    /// <remarks>
    /// A key that cannot be read is refused rather than skipped silently. Startup already proved every reference
    /// resolves and every declaration is well formed, so reaching either fault here means the deployment changed
    /// underneath a running process, which an operator has to see.
    /// </remarks>
    private async Task<ApiKeyAuthenticationResult> MatchAsync(
        ConfiguredSecret configuredKey,
        byte[] presentedDigest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!SecretName.TryCreate(configuredKey.Name, out var keyName))
        {
            this.LogKeyDeclarationUnusable();

            return ApiKeyAuthenticationResult.Rejected(ApiKeyRejection.CredentialUnrecognized);
        }

        var resolution = await this.secretReferenceResolver.ResolveAsync(
            configuredKey.SecretReference,
            cancellationToken);

        if (resolution.Secret is not { } material)
        {
            this.LogKeyMaterialUnavailable(keyName.Value!, resolution.Failure);

            return ApiKeyAuthenticationResult.Rejected(ApiKeyRejection.CredentialUnrecognized);
        }

        using (material)
        {
            if (!CryptographicOperations.FixedTimeEquals(this.DigestOfTextView(material), presentedDigest))
            {
                return ApiKeyAuthenticationResult.Rejected(ApiKeyRejection.CredentialUnrecognized);
            }
        }

        // A lifetime that no longer parses is treated as ended rather than as unbounded, so a deployment edited into an
        // unreadable state closes the endpoint instead of opening it.
        if (!SecretLifetime.TryParse(configuredKey.Lifetime, out var lifetime) || lifetime.HasExpiredAt(now))
        {
            this.LogExpiredKeyPresented(keyName.Value!);

            return ApiKeyAuthenticationResult.Rejected(ApiKeyRejection.CredentialExpired);
        }

        return ApiKeyAuthenticationResult.Authenticated(keyName);
    }

    /// <summary>Reduces configured key material to the digest of its text view.</summary>
    /// <remarks>
    /// <para>
    /// The text view rather than the raw bytes, because an API key is a credential a client writes into a header, and
    /// <see cref="ResolvedSecret" /> removes one trailing newline from that view for exactly the reason it matters here:
    /// a key file written by <c>echo</c>, provisioned as a Compose secret, or mounted by Kubernetes routinely ends in
    /// one. Digesting the bytes would compare a value no operator can see against the one they configured, and the
    /// deployment would start cleanly while refusing every client presenting the visible key.
    /// </para>
    /// <para>
    /// The decoded characters are held in a pinned buffer and cleared here rather than left for the collector, on the
    /// same terms as the encoded copy below and as every other secret this process handles.
    /// </para>
    /// </remarks>
    private byte[] DigestOfTextView(ResolvedSecret material)
    {
        var revealedText = GC.AllocateArray<char>(material.TextLength, pinned: true);

        try
        {
            material.RevealTextInto(revealedText);

            return this.DigestOfCharacters(revealedText);
        }
        finally
        {
            revealedText.AsSpan().Clear();
        }
    }

    /// <summary>Reduces text to its digest, erasing the encoded copy it had to make.</summary>
    /// <remarks>
    /// A presented credential arrives as a <see cref="string" />, which the request pipeline already materialized and
    /// which cannot be erased. What this controls is the second copy: the encoded bytes are held in a pinned buffer and
    /// zeroed here rather than left for the collector.
    /// </remarks>
    private byte[] DigestOfCharacters(ReadOnlySpan<char> text)
    {
        var encodedText = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(text), pinned: true);

        try
        {
            Encoding.UTF8.GetBytes(text, encodedText);

            return this.Digest(encodedText);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedText);
        }
    }

    private byte[] Digest(ReadOnlySpan<byte> material)
    {
        var digest = new byte[DigestLength];
        HMACSHA256.HashData(this.comparisonKey, material, digest);

        return digest;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A configured API key carries no usable name, so it cannot authenticate a request. Startup validates "
            + "this, which means the configuration changed underneath the running process.")]
    private partial void LogKeyDeclarationUnusable();

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The material behind API key {ApiKeyName} could not be retrieved, so that key cannot authenticate a "
            + "request [{Failure}].")]
    private partial void LogKeyMaterialUnavailable(string apiKeyName, SecretResolutionFailure? failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented API key {ApiKeyName}, whose configured lifetime has ended. The request was "
            + "refused with the same response as any other refusal; rotate the key or extend its lifetime.")]
    private partial void LogExpiredKeyPresented(string apiKeyName);
}
