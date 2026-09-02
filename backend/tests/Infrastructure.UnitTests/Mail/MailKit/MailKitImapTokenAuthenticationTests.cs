// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit.Security;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>
/// Covers the claim the OAuth path rests on: a rejected access token is renewed exactly once, and a second rejection
/// fails the attempt rather than looping. Both halves matter — without the renewal a revoked token strands the
/// account until a restart, and without the bound a permanently refused one spends the establishment budget.
/// </summary>
public sealed class MailKitImapTokenAuthenticationTests
{
    private static readonly DateTimeOffset TokenExpiry = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OpenReadOnlyAsync_ServerAdvertisesOAuthBearer_AuthenticatesWithATokenAndNoPassword()
    {
        // Arrange
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = CreateTokenAuthenticatingClient();
        var tokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await using var session = await OpenAsync(resilience, client, tokenSource);

        // Assert
        Assert.Equal(["OAUTHBEARER"], client.SaslMechanismNames);
        Assert.Equal(["access-token-1"], client.PresentedAccessTokens);
        Assert.Equal(0, tokenSource.RenewCount);
        await client.Client.DidNotReceive().AuthenticateAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerRejectsAnUnexpiredToken_RenewsOnceAndPresentsTheReplacement()
    {
        // Arrange
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = CreateTokenAuthenticatingClient();
        client.RefusedSaslAuthenticationCount = 1;
        var tokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await using var session = await OpenAsync(resilience, client, tokenSource);

        // Assert: the second attempt presented the renewed token rather than repeating the refused one.
        Assert.Equal(["access-token-1", "access-token-2"], client.PresentedAccessTokens);
        Assert.Equal(1, tokenSource.RenewCount);
        Assert.Equal(["access-token-1"], tokenSource.RejectedTokens);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerRejectsTheRenewedTokenToo_FailsAfterExactlyOneRenewal()
    {
        // Arrange
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = CreateTokenAuthenticatingClient();
        client.RefusedSaslAuthenticationCount = int.MaxValue;
        var tokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await Assert.ThrowsAsync<AuthenticationException>(() => OpenAsync(resilience, client, tokenSource));

        // Assert: two authentications and one renewal, so a permanently refused token cannot loop.
        Assert.Equal(2, client.PresentedAccessTokens.Count);
        Assert.Equal(1, tokenSource.RenewCount);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_RejectedTokenIsRenewed_TheRefusedValueIsNeverPresentedAgain()
    {
        // Arrange
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = CreateTokenAuthenticatingClient();
        client.RefusedSaslAuthenticationCount = 1;
        var tokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await using var session = await OpenAsync(resilience, client, tokenSource);

        // Assert
        Assert.Equal(
            client.PresentedAccessTokens.Count,
            client.PresentedAccessTokens.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Builds a server that advertises the registered token mechanism and nothing a password could use.</summary>
    private static FakeImapClient CreateTokenAuthenticatingClient()
    {
        var client = new FakeImapClient();
        client.AuthenticationMechanisms.Add("OAUTHBEARER");
        client.AuthenticationMechanisms.Add("PLAIN");

        return client;
    }

    private static Task<IMailboxSession> OpenAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        RecordingMailAccessTokenSource tokenSource)
    {
        client.Folder = MailKitImapSessionTestContext.CreateSelectedFolder();

        var factory = MailKitImapSessionTestContext.CreateFactory(
            resilience,
            () => client.Client,
            MailKitImapSessionTestContext.CreateSettingsProvider(),
            tokenSource);

        return factory.OpenReadOnlyAsync(
            MailKitImapSessionTestContext.PrimaryAccount,
            MailKitImapSessionTestContext.InboxFolder,
            MailKitImapSessionTestContext.TlsOnConnectWithOAuthBearerPolicy,
            CancellationToken.None);
    }
}
