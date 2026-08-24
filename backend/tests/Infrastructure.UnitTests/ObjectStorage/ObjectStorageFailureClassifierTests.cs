// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers the five facts an operator acts on differently, and the honest sixth for everything else.</summary>
public sealed class ObjectStorageFailureClassifierTests
{
    /// <summary>A caller that went away is a fact about the caller, and reporting it as an endpoint failure would take an instance out of traffic over it.</summary>
    [Fact]
    public void Classify_CancellationTheCallerAskedFor_IsTheCallers()
    {
        // Arrange
        using var caller = new CancellationTokenSource();
        using var shutdown = new CancellationTokenSource();
        caller.Cancel();

        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new OperationCanceledException(),
            caller.Token,
            shutdown.Token);

        // Assert
        Assert.Equal(ObjectStorageFailure.CallerCancelled, classification);
    }

    /// <summary>A request nobody is waiting for is nobody's work to resume, so the caller is asked about before the host is.</summary>
    [Fact]
    public void Classify_CancellationDuringShutdownTheCallerAlsoAskedFor_IsStillTheCallers()
    {
        // Arrange
        using var caller = new CancellationTokenSource();
        using var shutdown = new CancellationTokenSource();
        caller.Cancel();
        shutdown.Cancel();

        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new OperationCanceledException(),
            caller.Token,
            shutdown.Token);

        // Assert
        Assert.Equal(ObjectStorageFailure.CallerCancelled, classification);
    }

    [Fact]
    public void Classify_CancellationTheHostAskedFor_IsAShutdown()
    {
        // Arrange
        using var caller = new CancellationTokenSource();
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new OperationCanceledException(),
            caller.Token,
            shutdown.Token);

        // Assert
        Assert.Equal(ObjectStorageFailure.HostShuttingDown, classification);
    }

    /// <summary>
    /// A cancellation nobody asked for is a budget that came due, which .NET gives the same type as the two above. Only
    /// the tokens separate them, and only this one is worth attempting again.
    /// </summary>
    [Fact]
    public void Classify_CancellationNobodyAskedFor_IsABudgetThatCameDue()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new OperationCanceledException(),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TimedOut, classification);
        Assert.True(classification.IsWorthRepeating);
    }

    [Fact]
    public void Classify_ATimeout_IsABudgetThatCameDue()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new TimeoutException("the endpoint took too long"),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TimedOut, classification);
    }

    /// <summary>
    /// A status is not enough on its own: S3 answers a wrong signature and an object under a policy granting no listing
    /// with the same <c>403</c>, and the code is what names the credential as the thing an operator has to repair.
    /// </summary>
    [Theory]
    [InlineData("AccessDenied")]
    [InlineData("AccountProblem")]
    [InlineData("AuthorizationHeaderMalformed")]
    [InlineData("ExpiredToken")]
    [InlineData("InvalidAccessKeyId")]
    [InlineData("InvalidSecurity")]
    [InlineData("RequestTimeTooSkewed")]
    [InlineData("SignatureDoesNotMatch")]
    [InlineData("TokenRefreshRequired")]
    public void Classify_AnEndpointErrorCodeNamingTheCredential_IsAuthenticationFailed(string endpointErrorCode)
    {
        // Arrange: a 400 rather than a 403, so the code alone is what decides.
        var refusal = Answer(HttpStatusCode.BadRequest, endpointErrorCode);

        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            refusal,
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.AuthenticationFailed, classification);
        Assert.False(classification.IsWorthRepeating);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Classify_AnEndpointRefusingTheRequestOutright_IsAuthenticationFailed(HttpStatusCode status)
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            Answer(status, errorCode: string.Empty),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.AuthenticationFailed, classification);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void Classify_AnEndpointInvitingTheRequestAgain_IsWorthRepeating(HttpStatusCode status)
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            Answer(status, errorCode: string.Empty),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, classification);
        Assert.True(classification.IsWorthRepeating);
    }

    [Fact]
    public void Classify_AnEndpointAnsweringRequestTimeout_IsABudgetThatCameDue()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            Answer(HttpStatusCode.RequestTimeout, errorCode: string.Empty),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TimedOut, classification);
    }

    /// <summary>The SDK reports a request that failed before an answer arrived as the same type with no status, which is transport rather than a decision the endpoint took.</summary>
    [Fact]
    public void Classify_AServiceFailureCarryingNoStatus_IsTransport()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new AmazonServiceException("the request never reached the endpoint"),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, classification);
    }

    /// <summary>An answer the endpoint composed that means none of the above is terminal, because a repetition receives the same one.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public void Classify_AnEndpointRejectingTheRequestOnItsMerits_IsUnrecognized(HttpStatusCode status)
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            Answer(status, errorCode: "NoSuchBucket"),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.Unrecognized, classification);
        Assert.False(classification.IsWorthRepeating);
    }

    /// <summary>The SDK's client-side failure is the one it raises when a request could not be completed at all.</summary>
    [Fact]
    public void Classify_AClientFailure_IsTransport()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new AmazonClientException("the connection was closed"),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, classification);
    }

    [Fact]
    public void Classify_ALostTransport_IsWorthRepeating()
    {
        // Arrange
        Exception[] lostTransports =
        [
            new HttpRequestException("no route to host"),
            new SocketException((int)SocketError.ConnectionReset),
            new IOException("the stream ended mid-response"),
        ];

        // Act
        var classifications = lostTransports.Select(failure => ObjectStorageFailureClassifier.Classify(
            failure,
            CancellationToken.None,
            CancellationToken.None));

        // Assert
        Assert.All(
            classifications,
            classification => Assert.Equal(ObjectStorageFailure.TransientTransportFailure, classification));
    }

    /// <summary>Everything unrecognized is terminal, on the reasoning every other family in this system follows.</summary>
    [Fact]
    public void Classify_AFailureThisSystemDoesNotRecognize_IsTerminal()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new InvalidOperationException("something else entirely"),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.Unrecognized, classification);
        Assert.False(classification.IsWorthRepeating);
    }

    /// <summary>An operation that outlived the budget the pipeline gave it timed out, whatever the endpoint would have said.</summary>
    [Fact]
    public void Classify_APipelineThatSpentTheWholeTimeBudget_IsATimeout()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new OutboundDependencyUnavailableException(
                OutboundDependency.ObjectStorageInvocation,
                new TimeoutRejectedException("the budget was spent")),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TimedOut, classification);
    }

    /// <summary>
    /// An open circuit and a shed execution are this process declining to call the endpoint rather than a failure it
    /// does not recognize, and a readiness scrape asking again is exactly what resolves one.
    /// </summary>
    [Fact]
    public void Classify_APipelineThatRefusedToCallTheEndpoint_IsTransportRatherThanUnrecognized()
    {
        // Act
        var classification = ObjectStorageFailureClassifier.Classify(
            new OutboundDependencyUnavailableException(
                OutboundDependency.ObjectStorageInvocation,
                new BrokenCircuitException("the circuit is open")),
            CancellationToken.None,
            CancellationToken.None);

        // Assert
        Assert.Equal(ObjectStorageFailure.TransientTransportFailure, classification);
        Assert.True(classification.IsWorthRepeating);
    }

    [Fact]
    public void Classify_NoFailure_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ObjectStorageFailureClassifier.Classify(
                failure: null!,
                CancellationToken.None,
                CancellationToken.None));
    }

    private static AmazonServiceException Answer(HttpStatusCode status, string errorCode) => new(
        "the endpoint answered",
        ErrorType.Unknown,
        errorCode,
        "request-id",
        status);
}
