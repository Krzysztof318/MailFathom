// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.OAuth;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.OAuth;

public sealed class MailAccessTokenCacheTests
{
    private const string PrimaryAccount = "primary";
    private const string SecondaryAccount = "secondary";

    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetOrIssueAsync_TokenStillFarFromExpiry_DoesNotReachTheAuthorizationServerAgain()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        var issueCount = 0;

        Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            issueCount++;

            return Task.FromResult(new MailAccessToken($"token-{issueCount}", Now.AddHours(1)));
        }

        // Act
        var first = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);
        var second = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Assert
        Assert.Equal(1, issueCount);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetOrIssueAsync_TokenInsideTheRefreshSkew_IssuesAReplacementBeforeItExpires()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        var issueCount = 0;

        Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            issueCount++;

            return Task.FromResult(new MailAccessToken($"token-{issueCount}", timeProvider.GetUtcNow().AddSeconds(90)));
        }

        await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Act: 40 seconds of a 90-second token remain, which is inside the one-minute skew but not yet expired.
        timeProvider.Advance(TimeSpan.FromSeconds(50));
        var refreshed = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Assert
        Assert.Equal(2, issueCount);
        Assert.Equal("token-2", refreshed.Value);
    }

    [Fact]
    public async Task RenewAsync_CachedTokenIsStillValid_IssuesAReplacementAnyway()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        var issueCount = 0;

        Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            issueCount++;

            return Task.FromResult(new MailAccessToken($"token-{issueCount}", Now.AddHours(1)));
        }

        var rejected = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Act: the mail server rejected a token this process believes is valid, which no expiry instant predicts.
        var renewed = await cache.RenewAsync(PrimaryAccount, rejected, IssueAsync, CancellationToken.None);

        // Assert
        Assert.Equal(2, issueCount);
        Assert.Equal("token-2", renewed.Value);
    }

    [Fact]
    public async Task RenewAsync_AfterRenewal_TheReplacementIsWhatLaterCallersRead()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        var issueCount = 0;

        Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            issueCount++;

            return Task.FromResult(new MailAccessToken($"token-{issueCount}", Now.AddHours(1)));
        }

        var initial = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);
        await cache.RenewAsync(PrimaryAccount, initial, IssueAsync, CancellationToken.None);

        // Act
        var subsequent = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Assert
        Assert.Equal(2, issueCount);
        Assert.Equal("token-2", subsequent.Value);
    }

    [Fact]
    public async Task GetOrIssueAsync_TwoAccounts_KeepsOneAccountsTokenOutOfTheOthers()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);

        // Act
        var primary = await cache.GetOrIssueAsync(
            PrimaryAccount,
            _ => Task.FromResult(new MailAccessToken("primary-token", Now.AddHours(1))),
            CancellationToken.None);
        var secondary = await cache.GetOrIssueAsync(
            SecondaryAccount,
            _ => Task.FromResult(new MailAccessToken("secondary-token", Now.AddHours(1))),
            CancellationToken.None);

        // Assert
        Assert.Equal("primary-token", primary.Value);
        Assert.Equal("secondary-token", secondary.Value);
    }

    [Fact]
    public async Task GetOrIssueAsync_ConcurrentCallersForOneAccount_ReachTheAuthorizationServerOnce()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        using var releaseIssuing = new SemaphoreSlim(0, 1);
        var issueCount = 0;

        async Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref issueCount);
            await releaseIssuing.WaitAsync(cancellationToken);

            return new MailAccessToken("token", Now.AddHours(1));
        }

        // Act: both callers are inside the gate before either can complete, which is the burst a folder run produces.
        var concurrentRequests = Enumerable
            .Range(0, 2)
            .Select(_ => cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None))
            .ToArray();

        releaseIssuing.Release();
        var tokens = await Task.WhenAll(concurrentRequests);

        // Assert
        Assert.Equal(1, issueCount);
        Assert.All(tokens, token => Assert.Equal("token", token.Value));
    }

    /// <summary>
    /// A revoked refresh token or a changed mailbox password rejects every live connection for the account at once,
    /// and each one asks for a renewal. They are serialized by the gate either way; what this proves is that the ones
    /// behind the first accept the replacement it fetched instead of spending a request each.
    /// </summary>
    [Fact]
    public async Task RenewAsync_ConcurrentCallersRejectingTheSameToken_ReachTheAuthorizationServerOnce()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        var issueCount = 0;

        Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref issueCount);

            return Task.FromResult(new MailAccessToken($"token-{issueCount}", Now.AddHours(1)));
        }

        var rejected = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Act: three connections meet the same refusal and all ask for a replacement.
        var renewals = await Task.WhenAll(Enumerable
            .Range(0, 3)
            .Select(_ => cache.RenewAsync(PrimaryAccount, rejected, IssueAsync, CancellationToken.None))
            .ToArray());

        // Assert: one issue for the original and one for the shared replacement.
        Assert.Equal(2, issueCount);
        Assert.All(renewals, token => Assert.Equal("token-2", token.Value));
        Assert.DoesNotContain(renewals, token => token.Value == rejected.Value);
    }

    [Fact]
    public async Task RenewAsync_CachedTokenIsTheOneThatWasRejected_IssuesAReplacementRatherThanReturningIt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var cache = new MailAccessTokenCache(timeProvider);
        var issueCount = 0;

        Task<MailAccessToken> IssueAsync(CancellationToken cancellationToken)
        {
            issueCount++;

            return Task.FromResult(new MailAccessToken($"token-{issueCount}", Now.AddHours(1)));
        }

        var rejected = await cache.GetOrIssueAsync(PrimaryAccount, IssueAsync, CancellationToken.None);

        // Act
        var renewed = await cache.RenewAsync(PrimaryAccount, rejected, IssueAsync, CancellationToken.None);

        // Assert: the cached value looked fresh by its expiry, and was still not an answer to the refusal.
        Assert.Equal(2, issueCount);
        Assert.NotEqual(rejected.Value, renewed.Value);
    }

    [Fact]
    public void IsDueForRefresh_TokenBeyondTheSkew_ReportsFalse()
    {
        // Arrange
        var token = new MailAccessToken("token", Now.AddMinutes(10));

        // Act, Assert
        Assert.False(token.IsDueForRefresh(Now, TimeSpan.FromMinutes(1)));
        Assert.True(token.IsDueForRefresh(Now.AddMinutes(9).AddSeconds(1), TimeSpan.FromMinutes(1)));
        Assert.True(token.IsDueForRefresh(Now.AddMinutes(11), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ToString_AccessToken_IsRedactedSoNoRecordCanPrintIt()
    {
        // Arrange
        var token = new MailAccessToken("a-real-looking-token", Now);

        // Act, Assert
        Assert.Equal("***", token.ToString());
        Assert.DoesNotContain("a-real-looking-token", token.ToString(), StringComparison.Ordinal);
    }
}
