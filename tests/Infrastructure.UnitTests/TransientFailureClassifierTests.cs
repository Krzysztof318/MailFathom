// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class TransientFailureClassifierTests
{
    private readonly TransientFailureClassifier classifier = new();

    public static TheoryData<OutboundDependency> MailDependencies =>
    [
        OutboundDependency.MailboxSessionEstablishment,
        OutboundDependency.MailboxDataRetrieval,
        OutboundDependency.EmailDelivery,
    ];

    public static TheoryData<OutboundDependency> EveryDependency => [.. Enum.GetValues<OutboundDependency>()];

    public static TheoryData<OutboundDependency> MailboxDependencies =>
    [
        OutboundDependency.MailboxSessionEstablishment,
        OutboundDependency.MailboxDataRetrieval,
    ];

    public static TheoryData<OutboundDependency> RepeatableDependencies =>
    [
        .. Enum.GetValues<OutboundDependency>().Where(dependency => dependency != OutboundDependency.EmailDelivery),
    ];

    public static TheoryData<Exception> AmbiguousDeliveryFailures =>
    [
        new SmtpProtocolException("The server closed the stream before its reply."),
        new SocketException((int)SocketError.ConnectionReset),
        new IOException("The stream was closed."),
        new ServiceNotConnectedException("The client is no longer connected."),
    ];

    /// <summary>Repeating a rejected credential against a mail server can lock the mailbox account.</summary>
    [Theory]
    [MemberData(nameof(MailDependencies))]
    public void IsTransientFailure_RejectedMailCredential_IsTerminal(OutboundDependency dependency)
    {
        // Arrange
        var failure = new AuthenticationException("The server rejected the credential.");

        // Act
        var isTransient = this.classifier.IsTransientFailure(dependency, failure);

        // Assert
        Assert.False(isTransient);
    }

    [Theory]
    [MemberData(nameof(MailDependencies))]
    public void IsTransientFailure_UnusableMailTlsHandshake_IsTerminal(OutboundDependency dependency)
    {
        // Arrange
        var failure = new SslHandshakeException("The certificate chain could not be validated.");

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(dependency, failure));
    }

    [Theory]
    [MemberData(nameof(MailDependencies))]
    public void IsTransientFailure_UnavailableAuthenticationMechanism_IsTerminal(OutboundDependency dependency)
    {
        // Arrange
        var failure = new MailAuthenticationMechanismUnavailableException("mailbox", ["SCRAM-SHA-256"]);

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(dependency, failure));
    }

    /// <summary>A server that refused a command will refuse the identical command again.</summary>
    [Fact]
    public void IsTransientFailure_RefusedImapCommand_IsTerminal()
    {
        // Arrange
        var failure = new ImapCommandException(ImapCommandResponse.No, "FETCH");

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(OutboundDependency.MailboxDataRetrieval, failure));
    }

    /// <summary>A desynchronized IMAP stream is cleared by the next session, unlike a rejected command.</summary>
    [Fact]
    public void IsTransientFailure_DroppedImapStream_IsWorthRepeating()
    {
        // Arrange
        var failure = new ImapProtocolException("The server unexpectedly closed the stream.");

        // Act, Assert
        Assert.True(this.classifier.IsTransientFailure(OutboundDependency.MailboxDataRetrieval, failure));
    }

    /// <summary>
    /// A connection lost after the message data cannot be told apart from one lost before it, and MailKit reports
    /// both as ordinary transport failures. Repeating either would risk a second copy in the recipient's mailbox.
    /// </summary>
    [Theory]
    [MemberData(nameof(AmbiguousDeliveryFailures))]
    public void IsTransientFailure_AmbiguousDeliveryOutcome_IsTerminal(Exception failure)
    {
        // Act
        var isTransient = this.classifier.IsTransientFailure(OutboundDependency.EmailDelivery, failure);

        // Assert
        Assert.False(isTransient);
    }

    /// <summary>RFC 5321 defines the 4yz reply class as a temporary rejection and the 5yz class as permanent.</summary>
    [Theory]
    [InlineData(SmtpStatusCode.ServiceNotAvailable, true)]
    [InlineData(SmtpStatusCode.MailboxBusy, true)]
    [InlineData(SmtpStatusCode.InsufficientStorage, true)]
    [InlineData(SmtpStatusCode.MailboxUnavailable, false)]
    [InlineData(SmtpStatusCode.TransactionFailed, false)]
    public void IsTransientFailure_SmtpRejection_FollowsTheReplyClass(SmtpStatusCode statusCode, bool expectedTransient)
    {
        // Arrange
        var failure = new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, statusCode, "The server rejected the submission.");

        // Act
        var isTransient = this.classifier.IsTransientFailure(OutboundDependency.EmailDelivery, failure);

        // Assert
        Assert.Equal(expectedTransient, isTransient);
    }

    /// <summary>A dropped mailbox connection costs nothing to repeat, unlike a dropped submission.</summary>
    [Theory]
    [MemberData(nameof(MailboxDependencies))]
    public void IsTransientFailure_LostMailboxConnection_IsWorthRepeating(OutboundDependency dependency)
    {
        // Arrange
        var failure = new ServiceNotConnectedException("The client is no longer connected.");

        // Act, Assert
        Assert.True(this.classifier.IsTransientFailure(dependency, failure));
    }

    [Theory]
    [MemberData(nameof(MailDependencies))]
    public void IsTransientFailure_UnauthenticatedMailService_IsTerminal(OutboundDependency dependency)
    {
        // Arrange
        var failure = new ServiceNotAuthenticatedException("The client is not authenticated.");

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(dependency, failure));
    }

    /// <summary>The provider states transience itself, so the classifier does not maintain a second SQLSTATE table.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsTransientFailure_DatabaseFailure_FollowsTheProvidersOwnVerdict(bool providerReportsTransient)
    {
        // Arrange
        var failure = Substitute.For<DbException>();
        failure.IsTransient.Returns(providerReportsTransient);

        // Act
        var isTransient = this.classifier.IsTransientFailure(OutboundDependency.DatabaseCommandExecution, failure);

        // Assert
        Assert.Equal(providerReportsTransient, isTransient);
    }

    /// <summary>The application's commit policy already retries a conflict; a second layer would multiply the attempts against the same rows.</summary>
    [Fact]
    public void IsTransientFailure_ConcurrencyConflict_IsTerminalForThePipeline()
    {
        // Arrange
        var failure = new PersistenceConcurrencyConflictException("A competing writer changed the same rows.");

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(OutboundDependency.DatabaseCommandExecution, failure));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void IsTransientFailure_ProviderResponse_FollowsTheHttpStatus(HttpStatusCode? statusCode, bool expectedTransient)
    {
        // Arrange
        var failure = new HttpRequestException(HttpRequestError.Unknown, message: null, inner: null, statusCode);

        // Act
        var isTransient = this.classifier.IsTransientFailure(OutboundDependency.AiProviderInvocation, failure);

        // Assert
        Assert.Equal(expectedTransient, isTransient);
    }

    /// <summary>Every class except delivery repeats a lost transport, because only a submission can already have succeeded.</summary>
    [Theory]
    [MemberData(nameof(RepeatableDependencies))]
    public void IsTransientFailure_LostTransport_IsWorthRepeating(OutboundDependency dependency)
    {
        // Arrange
        var failure = new SocketException((int)SocketError.ConnectionReset);

        // Act, Assert
        Assert.True(this.classifier.IsTransientFailure(dependency, failure));
    }

    /// <summary>A caller that cancelled is not a dependency that failed, and no family may blur that.</summary>
    [Theory]
    [MemberData(nameof(EveryDependency))]
    public void IsTransientFailure_CallerCancellation_IsNeverRepeated(OutboundDependency dependency)
    {
        // Arrange
        var failure = new OperationCanceledException();

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(dependency, failure));
    }

    /// <summary>An unrecognized failure repeated against a mail server is exactly what locks an account.</summary>
    [Theory]
    [MemberData(nameof(EveryDependency))]
    public void IsTransientFailure_UnrecognizedFailure_IsTerminal(OutboundDependency dependency)
    {
        // Arrange
        var failure = new InvalidOperationException("Something nobody classified.");

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(dependency, failure));
    }

    [Fact]
    public void IsTransientFailure_UndefinedDependency_FailsInsteadOfGuessingAFamily()
    {
        // Arrange
        var failure = new SocketException((int)SocketError.ConnectionReset);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => this.classifier.IsTransientFailure((OutboundDependency)99, failure));
    }

    [Fact]
    public void IsTransientFailure_NoFailure_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => this.classifier.IsTransientFailure(OutboundDependency.MailboxDataRetrieval, failure: null!));
    }


    /// <summary>
    /// An authorization server that answered with an RFC 6749 error code has decided: the refresh token is revoked,
    /// the client secret is wrong, or the scope was not granted. Repeating the request receives the same answer and
    /// spends the account's rate limit doing it.
    /// </summary>
    [Theory]
    [InlineData("invalid_grant")]
    [InlineData("invalid_client")]
    [InlineData("invalid_scope")]
    public void IsTransientFailure_AuthorizationServerRefusedTheGrant_IsTerminal(string authorizationServerErrorCode)
    {
        // Arrange
        var failure = new MailAccessTokenUnavailableException("primary", authorizationServerErrorCode);

        // Act
        var isTransient = this.classifier.IsTransientFailure(
            OutboundDependency.MailAuthorizationServerInvocation,
            failure);

        // Assert
        Assert.False(isTransient);
    }

    [Fact]
    public void IsTransientFailure_AuthorizationServerUnreachable_IsRepeatable()
    {
        // Arrange: no error code, because no response arrived to carry one.
        var failure = new MailAccessTokenUnavailableException("primary", new HttpRequestException("no route", null, HttpStatusCode.ServiceUnavailable));

        // Act
        var isTransient = this.classifier.IsTransientFailure(
            OutboundDependency.MailAuthorizationServerInvocation,
            failure);

        // Assert
        Assert.True(isTransient);
    }

    [Fact]
    public void IsTransientFailure_AuthorizationServerRejectedTheRequestOutright_IsTerminal()
    {
        // Arrange
        var failure = new MailAccessTokenUnavailableException("primary", new HttpRequestException("bad request", null, HttpStatusCode.BadRequest));

        // Act
        var isTransient = this.classifier.IsTransientFailure(
            OutboundDependency.MailAuthorizationServerInvocation,
            failure);

        // Assert
        Assert.False(isTransient);
    }

    [Fact]
    public void IsTransientFailure_TokenRefusalCarryingNeitherCodeNorCause_IsTerminal()
    {
        // Arrange
        var failure = new MailAccessTokenUnavailableException("primary", "empty_response");

        // Act, Assert
        Assert.False(this.classifier.IsTransientFailure(
            OutboundDependency.MailAuthorizationServerInvocation,
            failure));
    }
}
