// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
            : await this.IssueUnderAccountGateAsync(accountId, issueToken, rejectedToken: null, cancellationToken);
    }

    /// <summary>Replaces the token a mail server rejected, unless another caller has already replaced it.</summary>
    /// <param name="accountId">The normalized local account identifier.</param>
    /// <param name="rejectedToken">The token the mail server refused, which must not be handed back.</param>
    /// <param name="issueToken">Reaches the authorization server.</param>
    /// <param name="cancellationToken">Cancels waiting for the gate and the issuing.</param>
    /// <returns>A token that is not the rejected one.</returns>
    /// <remarks>
    /// The rejected token is passed in rather than the cache simply discarding what it holds, because a burst of
    /// connections for one account fails together: a revoked refresh token or a changed mailbox password rejects every
    /// live connection at once, and each one asks for a renewal. Comparing against what was refused lets the callers
    /// behind the first one through the gate accept the replacement it already fetched, instead of each spending its
    /// own request against an authorization server that rate-limits them.
    /// </remarks>
    public Task<MailAccessToken> RenewAsync(
        string accountId,
        MailAccessToken rejectedToken,
        Func<CancellationToken, Task<MailAccessToken>> issueToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(rejectedToken);
        ArgumentNullException.ThrowIfNull(issueToken);

        return this.IssueUnderAccountGateAsync(accountId, issueToken, rejectedToken, cancellationToken);
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

    /// <summary>Issues a token under the account's gate, accepting what a caller ahead of this one already fetched.</summary>
    /// <param name="accountId">The normalized local account identifier.</param>
    /// <param name="issueToken">Reaches the authorization server when no usable token is available.</param>
    /// <param name="rejectedToken">
    /// The token a mail server refused, when this is a renewal; <see langword="null" /> for an ordinary request. A
    /// cached token that is no longer this value was fetched by another caller after the rejection and answers the
    /// renewal; the same value does not, however fresh its expiry looks.
    /// </param>
    /// <param name="cancellationToken">Cancels waiting for the gate and the issuing.</param>
    /// <returns>A usable token.</returns>
    private async Task<MailAccessToken> IssueUnderAccountGateAsync(
        string accountId,
        Func<CancellationToken, Task<MailAccessToken>> issueToken,
        MailAccessToken? rejectedToken,
        CancellationToken cancellationToken)
    {
        var gate = this.accountGates.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            // While this caller waited, the one holding the gate may already have fetched the token it came for.
            if (this.TryReadUsableToken(accountId, out var tokenFetchedWhileWaiting)
                && !IsTheRejectedToken(tokenFetchedWhileWaiting, rejectedToken))
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

    /// <summary>Reports whether a cached token is the one a mail server just refused.</summary>
    /// <remarks>
    /// Compared by token value rather than by reference, because the cache is what both callers read and an equal
    /// value is the same credential whichever instance carries it.
    /// </remarks>
    private static bool IsTheRejectedToken(MailAccessToken cachedToken, MailAccessToken? rejectedToken) =>
        rejectedToken is not null && string.Equals(cachedToken.Value, rejectedToken.Value, StringComparison.Ordinal);
}
