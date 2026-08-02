// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Holds each account's current access token, and serializes the requests that replace it.</summary>
/// <remarks>
/// <para>
/// The cache is a process-wide singleton while the token source that fills it is scoped, because the two have
/// genuinely different lifetimes: a token is valid for every work unit on its account, whereas resolving the account's
/// settings reads the configuration snapshot the current scope captured. Keeping the state here is what lets the
/// source stay scoped without a singleton ever capturing a scoped dependency.
/// </para>
/// <para>
/// The gate is per account and never process-wide. A slow or unreachable authorization server for one mailbox must
/// not stall another account's synchronization, and one shared gate is exactly how it would. Within an account the
/// gate collapses a burst of connection attempts into one token request: the callers behind the first one find the
/// token it fetched.
/// </para>
/// </remarks>
internal sealed class MailAccessTokenCache : IDisposable
{
    /// <summary>How far before expiry a token stops being handed out.</summary>
    /// <remarks>
    /// The window has to exceed the time between presenting a token and the server validating it, plus any clock skew
    /// between this process and the authorization server. A minute is comfortably beyond both, and costs one extra
    /// refresh per token lifetime.
    /// </remarks>
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> accountGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MailAccessToken> tokensByAccount = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes an empty cache over the clock expiry is measured against.</summary>
    /// <param name="timeProvider">Supplies the current instant.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public MailAccessTokenCache(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Returns the cached token when one is usable, otherwise issues a replacement under the account's gate.</summary>
    /// <param name="accountId">The normalized local account identifier.</param>
    /// <param name="issueToken">Reaches the authorization server; called only when no usable token is cached.</param>
    /// <param name="cancellationToken">Cancels waiting for the gate and the issuing.</param>
    /// <returns>A token that is valid now.</returns>
    public async Task<MailAccessToken> GetOrIssueAsync(
        string accountId,
        Func<CancellationToken, Task<MailAccessToken>> issueToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(issueToken);

        return this.TryReadUsableToken(accountId, out var cachedToken)
            ? cachedToken
            : await this.IssueUnderAccountGateAsync(accountId, issueToken, acceptCached: true, cancellationToken);
    }

    /// <summary>Discards whatever is cached for one account and issues a replacement under its gate.</summary>
    /// <param name="accountId">The normalized local account identifier.</param>
    /// <param name="issueToken">Reaches the authorization server.</param>
    /// <param name="cancellationToken">Cancels waiting for the gate and the issuing.</param>
    /// <returns>A newly issued token.</returns>
    /// <remarks>A renewal was asked for because a mail server rejected what this cache believes, so no cached value answers it however fresh it looks.</remarks>
    public Task<MailAccessToken> RenewAsync(
        string accountId,
        Func<CancellationToken, Task<MailAccessToken>> issueToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(issueToken);

        return this.IssueUnderAccountGateAsync(accountId, issueToken, acceptCached: false, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var gate in this.accountGates.Values)
        {
            gate.Dispose();
        }

        this.accountGates.Clear();
        this.tokensByAccount.Clear();
    }

    private bool TryReadUsableToken(string accountId, out MailAccessToken token)
    {
        if (this.tokensByAccount.TryGetValue(accountId, out var cachedToken)
            && !cachedToken.IsDueForRefresh(this.timeProvider.GetUtcNow(), RefreshSkew))
        {
            token = cachedToken;

            return true;
        }

        token = null!;

        return false;
    }

    private async Task<MailAccessToken> IssueUnderAccountGateAsync(
        string accountId,
        Func<CancellationToken, Task<MailAccessToken>> issueToken,
        bool acceptCached,
        CancellationToken cancellationToken)
    {
        var gate = this.accountGates.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            // While this caller waited, the one holding the gate may already have fetched the token it came for.
            if (acceptCached && this.TryReadUsableToken(accountId, out var tokenFetchedWhileWaiting))
            {
                return tokenFetchedWhileWaiting;
            }

            var issuedToken = await issueToken(cancellationToken);
            this.tokensByAccount[accountId] = issuedToken;

            return issuedToken;
        }
        finally
        {
            gate.Release();
        }
    }
}
