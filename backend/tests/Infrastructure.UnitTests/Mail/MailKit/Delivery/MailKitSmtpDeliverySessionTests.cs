// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit.Net.Smtp;
using MailKit.Security;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Delivery;

/// <summary>Covers opening the one session able to reach a submission server, and what it reports about that server.</summary>
public sealed class MailKitSmtpDeliverySessionTests
{
    /// <summary>The three capabilities that decide whether a message may be sent are reported as facts, not as flags.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerDeclaringItsLimits_ReportsThemAsFacts()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        client.Capabilities.Returns(SmtpCapabilities.Size | SmtpCapabilities.EightBitMime | SmtpCapabilities.UTF8);
        client.MaxSize.Returns(35_882_577U);

        // Act
        await using var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new MailDeliveryCapabilities(35_882_577L, AcceptsEightBitContent: true, AcceptsInternationalizedAddresses: true),
            session.Capabilities);
    }

    /// <summary>A server advertising the size extension with no number enforces no fixed maximum rather than a maximum of nothing.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_SizeAdvertisedWithoutABound_ReportsNoMaximum()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        client.Capabilities.Returns(SmtpCapabilities.Size);
        client.MaxSize.Returns(0U);

        // Act
        await using var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(session.Capabilities.MaxMessageBytes);
        Assert.True(session.Capabilities.PermitsMessageOfSize(long.MaxValue));
    }

    /// <summary>The endpoint the transport is opened to and the encryption spoken over it both come from the settings and the policy.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_Always_ReachesTheConfiguredEndpointUnderThePolicy()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();

        // Act
        await using var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([(SmtpDeliveryTestContext.SubmissionHost, SmtpDeliveryTestContext.SubmissionPort)], transport.RequestedEndpoints);
        await client.Received(1).ConnectAsync(
            Arg.Any<System.Net.Sockets.Socket>(),
            SmtpDeliveryTestContext.SubmissionHost,
            SmtpDeliveryTestContext.SubmissionPort,
            SecureSocketOptions.SslOnConnect,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Every command over the established session is bounded by the client itself rather than left at a library default.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_Always_BoundsEveryLaterCommand()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        var timeouts = MailDeliveryTimeouts.Default with { Command = TimeSpan.FromSeconds(42) };

        // Act
        await using var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport, timeouts: timeouts)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                TestContext.Current.CancellationToken);

        // Assert
        client.Received().Timeout = 42_000;
    }

    /// <summary>A mechanism the operator's allow-list refuses is removed before the client can negotiate it.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_MechanismOutsideTheAllowList_IsNotLeftNegotiable()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN", "CRAM-MD5");
        using var transport = new ScriptedSubmissionTransport();

        // Act
        await using var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["PLAIN"], client.AuthenticationMechanisms);
    }

    /// <summary>
    /// SMTP has no clear-text command to fall back to, so a server offering nothing the account permits ends the
    /// attempt with the account's own coded failure instead of reaching the mail library with an emptied set.
    /// </summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerOfferingNoPermittedMechanism_FailsWithTheAccountsCodedFailure()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("CRAM-MD5");
        using var transport = new ScriptedSubmissionTransport();

        // Act
        var failure = await Assert.ThrowsAsync<MailAuthenticationMechanismUnavailableException>(() =>
            SmtpDeliveryTestContext
                .CreateFactory(resilience, client, transport)
                .OpenForDeliveryAsync(
                    SmtpDeliveryTestContext.Account,
                    SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                    TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(SmtpDeliveryTestContext.Account.Value, failure.Message, StringComparison.Ordinal);
        await client.DidNotReceive().AuthenticateAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account holding an OAuth credential presents the token mechanism rather than a password.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_AccountAuthenticatingWithAnAccessToken_PresentsTheTokenMechanism()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("OAUTHBEARER");
        using var transport = new ScriptedSubmissionTransport();
        var accessTokenSource = new RecordingMailAccessTokenSource(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

        // Act
        await using var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport, accessTokenSource: accessTokenSource)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithOAuthBearerPolicy,
                TestContext.Current.CancellationToken);

        // Assert
        await client.Received(1).AuthenticateAsync(
            Arg.Is<SaslMechanism>(mechanism => mechanism != null && mechanism.MechanismName == "OAUTHBEARER"),
            Arg.Any<CancellationToken>());
        await client.DidNotReceive().AuthenticateAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refusal is recorded as the numbers the server stated. The sentence beside them names the recipient it is
    /// about, and a log line that carried it would put an address into an operator's log at every bounced submission.
    /// </summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerRefusingTheCredential_RecordsTheCodesAndNotTheServersWords()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        client.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmtpCommandException(
                SmtpErrorCode.UnexpectedStatusCode,
                SmtpStatusCode.AuthenticationInvalidCredentials,
                "5.7.8 authentication failed for someone@example.test"));

        // Act
        await Assert.ThrowsAsync<SmtpCommandException>(() =>
            SmtpDeliveryTestContext
                .CreateFactory(resilience, client, transport)
                .OpenForDeliveryAsync(
                    SmtpDeliveryTestContext.Account,
                    SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                    TestContext.Current.CancellationToken));

        // Assert
        var recorded = Assert.Single(
            resilience.Logs.Records,
            record => record.Message.Contains("refused a command", StringComparison.Ordinal));
        Assert.Contains("535", recorded.Message, StringComparison.Ordinal);
        Assert.Contains("5.7.8", recorded.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SmtpRejectionDisposition.Permanent), recorded.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            recorded.Properties.Values,
            value => value?.ToString()?.Contains("someone@example.test", StringComparison.Ordinal) == true);
    }

    /// <summary>A stage that outlives its budget is a timeout naming that stage, so a hung server is never read as a shutdown.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_TransportThatNeverOpens_FailsWithATimeoutNamingTheStage()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        var factory = SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            socketConnector: async (_, _, connectionToken) =>
            {
                await Task.Delay(Timeout.Infinite, connectionToken);

                throw new InvalidOperationException("The transport was expected to be abandoned.");
            });

        // Act
        var opening = factory.OpenForDeliveryAsync(
            SmtpDeliveryTestContext.Account,
            SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            resilience.CompleteOnVirtualTimeAsync(opening, TimeSpan.FromSeconds(1)));

        // Assert
        Assert.Contains(nameof(MailDeliveryPhase.Connection), failure.Message, StringComparison.Ordinal);
        Assert.Contains(SmtpDeliveryTestContext.Account.Value, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A caller that stopped waiting is not a server that stopped answering, and the two must stay distinguishable.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_CallerCancelling_ReportsTheCancellationRatherThanATimeout()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var caller = new CancellationTokenSource();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        var factory = SmtpDeliveryTestContext.CreateFactory(
            resilience,
            client,
            transport,
            socketConnector: async (_, _, connectionToken) =>
            {
                await caller.CancelAsync();
                await Task.Delay(Timeout.Infinite, connectionToken);

                throw new InvalidOperationException("The transport was expected to be abandoned.");
            });

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.OpenForDeliveryAsync(
            SmtpDeliveryTestContext.Account,
            SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
            caller.Token));
    }

    /// <summary>A submission endpoint that keeps refusing for now spends the class's budget and reports the account as unavailable.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_ServerRefusingForNow_ReportsTheAccountAsUnavailable()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        client.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmtpCommandException(
                SmtpErrorCode.UnexpectedStatusCode,
                SmtpStatusCode.ServiceNotAvailable,
                "4.3.2 try again later"));

        // Act
        var failure = await Assert.ThrowsAsync<MailDeliveryUnavailableException>(() =>
            SmtpDeliveryTestContext
                .CreateFactory(resilience, client, transport)
                .OpenForDeliveryAsync(
                    SmtpDeliveryTestContext.Account,
                    SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                    TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SmtpDeliveryTestContext.Account, failure.AccountId);
        Assert.IsType<SmtpCommandException>(failure.InnerException);
    }

    /// <summary>An establishment that failed leaves no client behind, so a refused deployment does not leak a connection per attempt.</summary>
    [Fact]
    public async Task OpenForDeliveryAsync_EstablishmentFailure_ReleasesTheClientAndTheCredential()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        client.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AuthenticationException("The credential was refused."));
        var settingsProvider = SmtpDeliveryTestContext.CreateSettingsProvider(out var resolvedMaterial);

        // Act
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            SmtpDeliveryTestContext
                .CreateFactory(resilience, client, transport, settingsProvider: settingsProvider)
                .OpenForDeliveryAsync(
                    SmtpDeliveryTestContext.Account,
                    SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                    TestContext.Current.CancellationToken));

        // Assert
        client.Received(1).Dispose();
        Assert.All(resolvedMaterial, material => Assert.Throws<ObjectDisposedException>(() => material.Password!.RevealAsString()));
    }

    /// <summary>Disposing an open session ends the exchange politely and releases the client with it.</summary>
    [Fact]
    public async Task DisposeAsync_OpenSession_QuitsAndReleasesTheClient()
    {
        // Arrange
        using var resilience = SmtpDeliveryTestContext.CreateSingleAttemptResilience();
        using var client = SmtpDeliveryTestContext.CreateClient("PLAIN");
        using var transport = new ScriptedSubmissionTransport();
        client.IsConnected.Returns(true);
        var session = await SmtpDeliveryTestContext
            .CreateFactory(resilience, client, transport)
            .OpenForDeliveryAsync(
                SmtpDeliveryTestContext.Account,
                SmtpDeliveryTestContext.TlsOnConnectWithPlainPolicy,
                TestContext.Current.CancellationToken);

        // Act
        await session.DisposeAsync();

        // Assert
        await client.Received(1).DisconnectAsync(true, Arg.Any<CancellationToken>());
        client.Received(1).Dispose();
    }
}
