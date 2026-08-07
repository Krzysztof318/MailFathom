// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the classification the retry decision and the operator's next move both rest on.</summary>
public sealed class EmbeddingProviderFailureClassificationTests
{
    /// <summary>
    /// The provider libraries surface a refusal as their own result type carrying the status, so the status is the
    /// whole of the evidence. `403` joins `401` because the operator's move is the same for both and repeating either
    /// buys the same answer.
    /// </summary>
    [Theory]
    [InlineData(401, EmbeddingGenerationFailure.CredentialRejected)]
    [InlineData(403, EmbeddingGenerationFailure.CredentialRejected)]
    [InlineData(429, EmbeddingGenerationFailure.RateLimited)]
    [InlineData(408, EmbeddingGenerationFailure.RequestTimedOut)]
    [InlineData(504, EmbeddingGenerationFailure.RequestTimedOut)]
    [InlineData(500, EmbeddingGenerationFailure.TransportFaulted)]
    [InlineData(503, EmbeddingGenerationFailure.TransportFaulted)]
    [InlineData(400, EmbeddingGenerationFailure.RequestRefused)]
    [InlineData(404, EmbeddingGenerationFailure.RequestRefused)]
    [InlineData(0, EmbeddingGenerationFailure.TransportFaulted)]
    public void Classify_AProviderRefusal_ReadsItsStatus(int status, EmbeddingGenerationFailure expected)
    {
        // Arrange
        using var response = new FakeClientResponse(status);
        var failure = new ClientResultException("refused", response);

        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(failure);

        // Assert
        Assert.Equal(expected, classified);
    }

    [Fact]
    public void Classify_AnHttpFailureWithoutAStatus_IsATransportFault()
    {
        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(new HttpRequestException("unreachable"));

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.TransportFaulted, classified);
    }

    [Fact]
    public void Classify_AnHttpFailureCarryingAStatus_ReadsIt()
    {
        // Arrange
        var failure = new HttpRequestException("throttled", inner: null, HttpStatusCode.TooManyRequests);

        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(failure);

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.RateLimited, classified);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public void Classify_ATransportFailure_IsATransportFault(Exception failure)
    {
        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(failure);

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.TransportFaulted, classified);
    }

    [Fact]
    public void Classify_ATimeout_IsARequestTimeout()
    {
        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(new TimeoutException());

        // Assert
        Assert.Equal(EmbeddingGenerationFailure.RequestTimedOut, classified);
    }

    /// <summary>
    /// A caller's own cancellation and a host shutdown are this system's decision rather than the provider's, so
    /// classifying one as a provider failure would let it open a circuit against a healthy endpoint.
    /// </summary>
    [Fact]
    public void Classify_ACancellation_IsNotAProviderFailure()
    {
        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(new OperationCanceledException());

        // Assert
        Assert.Null(classified);
    }

    [Fact]
    public void Classify_AFailureNoProviderProduced_IsUnclassified()
    {
        // Act
        var classified = EmbeddingProviderFailureClassification.Classify(new InvalidOperationException());

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
