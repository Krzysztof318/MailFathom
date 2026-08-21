// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit.Security;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Delivery;

/// <summary>
/// Covers what a submission does about an access token its server refuses. Without a renewal a rotated token is
/// presented on every send until the process-wide cache entry expires on its own, so each one fails; with an unbounded
/// one a permanently refused credential would loop instead of failing the attempt. Where the renewal sits relative to
/// the stage budgets is asserted here too, because a token exchange inside the Authentication stage would report the
/// authorization server's silence as the submission server's.
/// </summary>
public sealed class MailKitSmtpTokenAuthenticationTests
{
    private static readonly DateTimeOffset TokenExpiry = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An accepted token is presented once, and nothing is renewed.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerAcceptingTheCachedToken_PresentsItOnceAndRenewsNothing()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("OAUTHBEARER");
        using var transport = new ScriptedSubmissionTransport();
        var presentedAccessTokens = SmtpDeliveryTestContext.ScriptTokenAuthentication(client);
        var accessTokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await using var session = await OpenAsync(SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            accessTokenSource: accessTokenSource));

        // Assert
        Assert.Equal(["access-token-1"], presentedAccessTokens);
        Assert.Equal(0, accessTokenSource.RenewCount);
    }

    /// <summary>The case the renewal exists for: a token this process believed was valid, refused by the server.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerRejectsAnUnexpiredToken_RenewsOnceAndPresentsTheReplacement()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("OAUTHBEARER");
        using var transport = new ScriptedSubmissionTransport();
        var presentedAccessTokens = SmtpDeliveryTestContext.ScriptTokenAuthentication(
            client,
            refusedAuthenticationCount: 1);
        var accessTokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await using var session = await OpenAsync(SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            accessTokenSource: accessTokenSource));

        // Assert: the second attempt presented the renewed token rather than repeating the refused one.
        Assert.Equal(["access-token-1", "access-token-2"], presentedAccessTokens);
        Assert.Equal(1, accessTokenSource.RenewCount);
        Assert.Equal(["access-token-1"], accessTokenSource.RejectedTokens);
    }

    /// <summary>A credential the authorization server and the mail server agree on fails the attempt rather than looping.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerRejectsTheRenewedTokenToo_FailsAfterExactlyOneRenewal()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("OAUTHBEARER");
        using var transport = new ScriptedSubmissionTransport();
        var presentedAccessTokens = SmtpDeliveryTestContext.ScriptTokenAuthentication(
            client,
            refusedAuthenticationCount: int.MaxValue);
        var accessTokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            OpenAsync(SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            accessTokenSource: accessTokenSource)));

        // Assert
        Assert.Equal(2, presentedAccessTokens.Count);
        Assert.Equal(1, accessTokenSource.RenewCount);
        Assert.Equal(
            presentedAccessTokens.Count,
            presentedAccessTokens.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The exchange is bounded by the authorization server's own class, not by the Authentication stage, so an
    /// exchange slower than that stage's budget still ends in an authenticated session.
    /// </summary>
    [Fact]
    public async Task OpenForDeliveryAsync_TokenExchangeOutlastingTheAuthenticationBudget_StillAuthenticates()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("OAUTHBEARER");
        using var transport = new ScriptedSubmissionTransport();
        var presentedAccessTokens = SmtpDeliveryTestContext.ScriptTokenAuthentication(client);
        var accessTokenSource = new RecordingMailAccessTokenSource(TokenExpiry)
        {
            WhileExchanging = exchangeToken => Task.Delay(
                MailDeliveryTimeouts.Default.Authentication + TimeSpan.FromSeconds(5),
                resilience.TimeProvider,
                exchangeToken),
        };

        // Act
        var opening = OpenAsync(SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            accessTokenSource: accessTokenSource));
        await using var session = await resilience.CompleteOnVirtualTimeAsync(opening, TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(["access-token-1"], presentedAccessTokens);
    }

    /// <summary>Every round trip to the submission server still carries the stage budget, and reports it by name.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerNeverAnsweringTheCredential_FailsWithATimeoutNamingTheAuthenticationStage()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("OAUTHBEARER");
        using var transport = new ScriptedSubmissionTransport();
        client.AuthenticateAsync(Arg.Any<SaslMechanism>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));
        var accessTokenSource = new RecordingMailAccessTokenSource(TokenExpiry);

        // Act
        var opening = OpenAsync(SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            accessTokenSource: accessTokenSource));

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            resilience.CompleteOnVirtualTimeAsync(opening, TimeSpan.FromSeconds(1)));

        // Assert
        Assert.Contains(nameof(MailDeliveryPhase.Authentication), failure.Message, StringComparison.Ordinal);
        Assert.Contains(SmtpDeliveryTestContext.Account.Value, failure.Message, StringComparison.Ordinal);
    }

    private static Task<IMailDeliverySession> OpenAsync(MailKitSmtpDeliverySessionFactory factory) =>
        factory.OpenForDeliveryAsync(
            SmtpDeliveryTestContext.Account,
            SmtpDeliveryTestContext.TlsOnConnectWithOAuthBearerPolicy,
            CancellationToken.None);
}
