// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>Judges the credential a request presented against the passwords this deployment stores for its owners.</summary>
/// <remarks>
/// <para>
/// Every rule worth asserting about HTTP Basic on this deployment lives here rather than in the handler above it:
/// what a readable credential is, what a username folds to, how many attempts a source gets, what the stored record is
/// compared with, and what a refusal is allowed to distinguish. A test reaches all of it without a request pipeline.
/// </para>
/// <para>
/// <strong>One failure path.</strong> An unknown username, a wrong password, a credential somebody disabled, and a
/// credential whose owner record has since been removed all return
/// <see cref="OwnerPasswordRejection.CredentialUnrecognized" />, and each of them spends one key derivation before it
/// does. That is what <see cref="DecoyPasswordHash" /> is for: a username that resolves nothing would otherwise be
/// refused in the time of one indexed read, and a client timing the difference would be enumerating the accounts this
/// deployment holds. The last of the four cases cannot arise while the credential row's foreign key stands — removing
/// an owner removes their credentials with them — and it is refused along the same path anyway rather than being
/// modelled as something that could be reported.
/// </para>
/// <para>
/// <strong>Cheap refusals first.</strong> The header is read, bounded, and folded into a canonical username before any
/// capacity is spent, and capacity is spent before any password is verified. An unauthenticated caller therefore
/// cannot make this process perform a deliberately expensive derivation by writing a malformed header, and cannot make
/// it perform an unbounded number of them by writing well-formed ones.
/// </para>
/// <para>
/// <strong>Nothing written down is the credential.</strong> Neither the returned result nor anything logged on the way
/// to it carries the presented password, the presented username, or the stored hash. What a record names is the
/// credential's identifier where one was established safely, and the surface and the rejection where it was not.
/// </para>
/// </remarks>
public sealed partial class OwnerPasswordAuthenticator
{
    /// <summary>What a request with no remote address is counted under, which is a local or in-process caller.</summary>
    /// <remarks>A word rather than an empty key, so a bucket that stands for "no address" is visibly that rather than looking like a partition somebody forgot to fill.</remarks>
    internal const string UnknownSource = "unknown";

    private readonly IOwnerPasswordCredentialStore credentials;
    private readonly IPasswordHasher passwordHasher;
    private readonly PasswordAttemptLimiter attemptLimiter;
    private readonly DecoyPasswordHash decoy;
    private readonly ILogger<OwnerPasswordAuthenticator> logger;

    /// <summary>Initializes a new owner password authenticator.</summary>
    /// <param name="credentials">Where the owners' credentials are kept.</param>
    /// <param name="passwordHasher">What a presented password is judged with, and what a record behind the policy is rewritten by.</param>
    /// <param name="attemptLimiter">The bound on how often a source or a username may have a password checked.</param>
    /// <param name="decoy">The record an unresolved username is compared against, derived once for the process.</param>
    /// <param name="logger">The log a refusal and a rehash failure are recorded in.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument but the logger is <see langword="null" />.</exception>
    /// <remarks>Nothing is derived here, so constructing this per request costs what resolving four services costs.</remarks>
    public OwnerPasswordAuthenticator(
        IOwnerPasswordCredentialStore credentials,
        IPasswordHasher passwordHasher,
        PasswordAttemptLimiter attemptLimiter,
        DecoyPasswordHash decoy,
        ILogger<OwnerPasswordAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(attemptLimiter);
        ArgumentNullException.ThrowIfNull(decoy);

        this.credentials = credentials;
        this.passwordHasher = passwordHasher;
        this.attemptLimiter = attemptLimiter;
        this.decoy = decoy;
        this.logger = logger;
    }

    /// <summary>Judges the credential an <c>Authorization</c> header carried.</summary>
    /// <param name="surfaceName">The transport surface the request arrived on, which keeps two surfaces' attempt buckets apart.</param>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="source">The remote address the request came from, or <see langword="null" /> when it carried none.</param>
    /// <param name="attemptsPerMinute">How many attempts the surface allows one source and one username each minute.</param>
    /// <param name="cancellationToken">Cancels the credential read and the rehash that may follow a success.</param>
    /// <returns>The credential and owner that matched, or the reason the credential was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="surfaceName" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attemptsPerMinute" /> is not positive, which would refuse every request rather than bounding any.</exception>
    public async Task<OwnerPasswordAuthenticationResult> AuthenticateAsync(
        string surfaceName,
        string? authorizationHeaderValue,
        string? source,
        int attemptsPerMinute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surfaceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptsPerMinute);

        // The credential owns the buffer its password was decoded into, so it is released on every path out of here
        // rather than only on the one that read it — which is what bounds the plaintext's life to this call.
        PresentedBasicCredential? presented = null;

        try
        {
            if (!BasicCredentialHeader.TryRead(authorizationHeaderValue, out presented))
            {
                return OwnerPasswordAuthenticationResult.Rejected(string.IsNullOrWhiteSpace(authorizationHeaderValue)
                    ? OwnerPasswordRejection.CredentialMissing
                    : OwnerPasswordRejection.CredentialMalformed);
            }

            if (!OwnerCredentialUsername.TryCreate(presented.UserId, out var username))
            {
                return OwnerPasswordAuthenticationResult.Rejected(OwnerPasswordRejection.UsernameUnusable);
            }

            var attempt = new PasswordAttempt(
                surfaceName,
                string.IsNullOrWhiteSpace(source) ? UnknownSource : source,
                username.Value,
                attemptsPerMinute);

            if (!this.attemptLimiter.TryBeginAttempt(attempt))
            {
                this.LogAttemptsExhausted(surfaceName);

                return OwnerPasswordAuthenticationResult.Rejected(OwnerPasswordRejection.TooManyAttempts);
            }

            return await this.JudgeAsync(username, presented, cancellationToken);
        }
        finally
        {
            presented?.Dispose();
        }
    }

    /// <summary>Resolves the username and compares the password, at one cost whatever the answer is.</summary>
    private async Task<OwnerPasswordAuthenticationResult> JudgeAsync(
        OwnerCredentialUsername username,
        PresentedBasicCredential presented,
        CancellationToken cancellationToken)
    {
        var credential = await this.credentials.FindByUsernameAsync(username, cancellationToken);

        // The decoy is verified rather than skipped, so a username nobody holds costs what a username somebody holds
        // costs. Its result is discarded because it can only ever be a failure.
        var verification = this.passwordHasher.Verify(
            credential?.PasswordHash ?? this.decoy.Value,
            presented.Password);

        if (credential is not { Enabled: true } || verification == PasswordVerification.Failed)
        {
            return OwnerPasswordAuthenticationResult.Rejected(OwnerPasswordRejection.CredentialUnrecognized);
        }

        if (verification == PasswordVerification.SucceededAndShouldBeRehashed)
        {
            await this.RehashAsync(credential, presented, cancellationToken);
        }

        return OwnerPasswordAuthenticationResult.Authenticated(credential.Id, credential.Owner);
    }

    /// <summary>Rewrites a record whose work parameters are behind the current policy, while the plaintext is still here.</summary>
    /// <remarks>
    /// This is the one moment a deployment can strengthen a stored password without asking anybody to choose a new one,
    /// so it is taken on the request that verified it. It cannot fail the request: the caller has already proved the
    /// password, and refusing them because a strengthening write did not commit would turn a raised iteration count
    /// into an outage. What a failure gets is a record naming the credential, so an operator sees a deployment whose
    /// records are not catching up.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A strengthening write that fails for any reason must leave the verified request served rather than turn a raised iteration count into an outage.")]
    private async Task RehashAsync(
        ResolvedOwnerPasswordCredential credential,
        PresentedBasicCredential presented,
        CancellationToken cancellationToken)
    {
        try
        {
            var rewritten = this.passwordHasher.Hash(presented.Password);

            await this.credentials.RewritePasswordHashAsync(
                credential.Owner,
                credential.Id,
                rewritten,
                cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The failure itself is deliberately not logged. A persistence fault carries the connection's own details in
            // its message and stack trace, and every ordinary logging provider renders both; only its type crosses over.
            this.LogRehashFailed(credential.Id, failure.GetType().Name);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A password attempt on the {TransportSurface} surface was refused without the password being checked, "
            + "because its source or the username it named has spent its attempts for the current period. Neither the "
            + "username nor the address is recorded; the bound is the endpoint's own Basic setting.")]
    private partial void LogAttemptsExhausted(string transportSurface);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The stored password of credential {CredentialId} verified under work parameters weaker than this "
            + "release writes, and rewriting it failed with {FailureType}. The request was served; the record stays as "
            + "it was and the next successful sign-in will try again.")]
    private partial void LogRehashFailed(Guid credentialId, string failureType);
}
