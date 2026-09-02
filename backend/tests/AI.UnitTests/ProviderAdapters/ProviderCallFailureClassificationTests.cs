// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using MailFathom.AI.ProviderAdapters;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the classification the retry decision and the operator's next move both rest on.</summary>
public sealed class ProviderCallFailureClassificationTests
{
    /// <summary>
    /// The provider libraries surface a refusal as their own result type carrying the status, so the status is the
    /// whole of the evidence. `403` joins `401` because the operator's move is the same for both and repeating either
    /// buys the same answer.
    /// </summary>
    /// <remarks>
    /// The expectation is the classification's name rather than the value, because the classification is internal to
    /// the AI boundary and a public test signature may not carry it. <c>nameof</c> keeps the reference a compile-time
    /// one, so a renamed member fails the build here rather than turning into a comparison against a stale string.
    /// </remarks>
    [Theory]
    [InlineData(401, nameof(ProviderCallFailure.CredentialRejected))]
    [InlineData(403, nameof(ProviderCallFailure.CredentialRejected))]
    [InlineData(429, nameof(ProviderCallFailure.RateLimited))]
    [InlineData(408, nameof(ProviderCallFailure.RequestTimedOut))]
    [InlineData(504, nameof(ProviderCallFailure.RequestTimedOut))]
    [InlineData(500, nameof(ProviderCallFailure.TransportFaulted))]
    [InlineData(503, nameof(ProviderCallFailure.TransportFaulted))]
    [InlineData(400, nameof(ProviderCallFailure.RequestRefused))]
    [InlineData(404, nameof(ProviderCallFailure.RequestRefused))]
    [InlineData(0, nameof(ProviderCallFailure.TransportFaulted))]
    public void Classify_AProviderRefusal_ReadsItsStatus(int status, string expected)
    {
        // Arrange
        using var response = new FakeClientResponse(status);
        var failure = new ClientResultException("refused", response);

        // Act
        var classified = ProviderCallFailureClassification.Classify(failure);

        // Assert
        Assert.Equal(expected, classified?.ToString());
    }

    [Fact]
    public void Classify_AnHttpFailureWithoutAStatus_IsATransportFault()
    {
        // Act
        var classified = ProviderCallFailureClassification.Classify(new HttpRequestException("unreachable"));

        // Assert
        Assert.Equal(ProviderCallFailure.TransportFaulted, classified);
    }

    [Fact]
    public void Classify_AnHttpFailureCarryingAStatus_ReadsIt()
    {
        // Arrange
        var failure = new HttpRequestException("throttled", inner: null, HttpStatusCode.TooManyRequests);

        // Act
        var classified = ProviderCallFailureClassification.Classify(failure);

        // Assert
        Assert.Equal(ProviderCallFailure.RateLimited, classified);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public void Classify_ATransportFailure_IsATransportFault(Exception failure)
    {
        // Act
        var classified = ProviderCallFailureClassification.Classify(failure);

        // Assert
        Assert.Equal(ProviderCallFailure.TransportFaulted, classified);
    }

    [Fact]
    public void Classify_ATimeout_IsARequestTimeout()
    {
        // Act
        var classified = ProviderCallFailureClassification.Classify(new TimeoutException());

        // Assert
        Assert.Equal(ProviderCallFailure.RequestTimedOut, classified);
    }

    /// <summary>
    /// A caller's own cancellation and a host shutdown are this system's decision rather than the provider's, so
    /// classifying one as a provider failure would let it open a circuit against a healthy endpoint.
    /// </summary>
    [Fact]
    public void Classify_ACancellation_IsNotAProviderFailure()
    {
        // Act
        var classified = ProviderCallFailureClassification.Classify(new OperationCanceledException());

        // Assert
        Assert.Null(classified);
    }

    [Fact]
    public void Classify_AFailureNoProviderProduced_IsUnclassified()
    {
        // Act
        var classified = ProviderCallFailureClassification.Classify(new InvalidOperationException());

        // Assert
        Assert.Null(classified);
    }

    public static TheoryData<Exception> TransportFailures() =>
    [
        new SocketException((int)SocketError.ConnectionReset),
        new IOException("the connection dropped mid-answer"),
    ];

    /// <summary>The one member of a provider response the classification reads, over a type with no public constructor.</summary>
    private sealed class FakeClientResponse(int status) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.Empty;

        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BinaryData.Empty);

        public override void Dispose()
        {
        }
    }
}
