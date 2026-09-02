// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OpenTelemetry.Exporter;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability.ClientTelemetry;

/// <summary>Covers what reaches the collector and what the client is told about it.</summary>
/// <remarks>
/// Both transports are exercised, because a deployment's exporter picks one and the proxy has to reach the same
/// collector the same way: over HTTP that is the batch posted verbatim, and over gRPC the same octets inside the frame
/// that protocol wraps a unary message in. The distinction the answers turn on is the specification's own — a rejection
/// the destination will never take back is relayed as one, and anything transient is answered as unavailability so the
/// client holds what it has not exported.
/// </remarks>
public sealed class ClientTelemetryForwarderTests
{
    private static readonly byte[] Batch = [0x0A, 0x02, 0x12, 0x00];

    /// <summary>The batch reaches the collector unchanged, at the path the specification serves the signal at.</summary>
    [Fact]
    public async Task ForwardAsync_OverHttp_PostsTheBatchVerbatimToTheSignalsOwnPath()
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.HttpProtobuf);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Metrics,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        var sent = Assert.Single(collector.RecordedRequests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://collector.example.test/v1/metrics"), sent.RequestUri);
        Assert.Equal(Batch, sent.Content.ToArray());
        Assert.True(forwarding.Arrived);
    }

    /// <summary>The collector's credential travels on the request, which is what keeps it out of the client bundle.</summary>
    [Fact]
    public async Task ForwardAsync_ADestinationWithConfiguredHeaders_SendsEachOfThem()
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        var forwarder = ForwarderOver(
            collector,
            OtlpExportProtocol.HttpProtobuf,
            [new KeyValuePair<string, string>("api-key", "abc123")]);

        // Act
        await forwarder.ForwardAsync(ClientTelemetrySignal.Logs, Batch, TestContext.Current.CancellationToken);

        // Assert
        var sent = Assert.Single(collector.RecordedRequests);
        Assert.Equal("abc123", Assert.Single(sent.Headers["api-key"]));
    }

    /// <summary>A partial success lives in the destination's own body, so answering a bare success would hide a rejection.</summary>
    [Fact]
    public async Task ForwardAsync_ADestinationReportingAPartialSuccess_RelaysWhatItAnswered()
    {
        // Arrange
        byte[] partialSuccess = [0x0A, 0x02, 0x08, 0x03];
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(partialSuccess) });
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.HttpProtobuf);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(forwarding.Arrived);
        Assert.Equal(partialSuccess, forwarding.Body);
    }

    /// <summary>The specification's own split — stop sending, or hold — plus the credential this deployment repairs itself.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, nameof(ClientTelemetryFailure.Refused))]
    [InlineData(HttpStatusCode.UnsupportedMediaType, nameof(ClientTelemetryFailure.Refused))]
    [InlineData(HttpStatusCode.TooManyRequests, nameof(ClientTelemetryFailure.Throttled))]
    [InlineData(HttpStatusCode.Unauthorized, nameof(ClientTelemetryFailure.Unauthorized))]
    [InlineData(HttpStatusCode.Forbidden, nameof(ClientTelemetryFailure.Unauthorized))]
    [InlineData(HttpStatusCode.BadGateway, nameof(ClientTelemetryFailure.Unavailable))]
    [InlineData(HttpStatusCode.ServiceUnavailable, nameof(ClientTelemetryFailure.Unavailable))]
    public async Task ForwardAsync_ADestinationThatDidNotAcceptTheBatch_ReportsTheConditionThatFitsIt(
        HttpStatusCode answered,
        string expected)
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(answered) { Content = new ByteArrayContent([]) });
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.HttpProtobuf);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, forwarding.Failure.ToString());
    }

    /// <summary>Nothing answering at the address is the condition an operator most needs named as itself.</summary>
    [Fact]
    public async Task ForwardAsync_ADestinationNothingAnswersAt_ReportsItAsUnreachable()
    {
        // Arrange
        using var collector = new FakeHttpMessageHandler(
            (_, _) => throw new HttpRequestException("nothing is listening"));
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.HttpProtobuf);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientTelemetryFailure.Unreachable, forwarding.Failure);
    }

    /// <summary>The gRPC frame is the whole difference between the two transports, and it has to survive both ways.</summary>
    [Fact]
    public async Task ForwardAsync_OverGrpc_FramesTheBatchAndTakesTheAnswerBackOutOfItsFrame()
    {
        // Arrange
        byte[] answer = [0x0A, 0x02, 0x08, 0x03];
        using var collector = FakeHttpMessageHandler.AlwaysResponding(() => GrpcAnswer(0, answer));
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.Grpc);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        var sent = Assert.Single(collector.RecordedRequests);
        Assert.Equal(
            new Uri("https://collector.example.test/opentelemetry.proto.collector.trace.v1.TraceService/Export"),
            sent.RequestUri);
        Assert.Equal([0, 0, 0, 0, (byte)Batch.Length, .. Batch], sent.Content.ToArray());
        Assert.True(forwarding.Arrived);
        Assert.Equal(answer, forwarding.Body);
    }

    /// <summary>A gRPC destination reports its verdict as a status rather than as a response code.</summary>
    [Theory]
    [InlineData(3, nameof(ClientTelemetryFailure.Refused))]
    [InlineData(12, nameof(ClientTelemetryFailure.Refused))]
    [InlineData(7, nameof(ClientTelemetryFailure.Unauthorized))]
    [InlineData(16, nameof(ClientTelemetryFailure.Unauthorized))]
    [InlineData(8, nameof(ClientTelemetryFailure.Throttled))]
    [InlineData(14, nameof(ClientTelemetryFailure.Unavailable))]
    public async Task ForwardAsync_OverGrpc_AStatusOtherThanOk_ReportsTheConditionThatFitsIt(
        int status,
        string expected)
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(() => GrpcAnswer(status, []));
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.Grpc);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, forwarding.Failure.ToString());
    }

    /// <summary>A success is the one thing a gRPC server states explicitly, so an answer stating nothing is not one.</summary>
    [Fact]
    public async Task ForwardAsync_OverGrpc_AnAnswerCarryingNoStatus_IsNotReadAsASuccess()
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });
        var forwarder = ForwarderOver(collector, OtlpExportProtocol.Grpc);

        // Act
        var forwarding = await forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ClientTelemetryFailure.Unavailable, forwarding.Failure);
    }

    /// <summary>The ceiling is the exporter's own, and a destination past it is a condition rather than a hung request.</summary>
    /// <remarks>
    /// The wait is the fake clock's rather than the machine's, which is what makes the deadline itself the thing under
    /// test: the collector is entered and then blocks until its token is cancelled, so nothing here completes until the
    /// clock is advanced past the configured timeout.
    /// </remarks>
    [Fact]
    public async Task ForwardAsync_ADestinationThatDoesNotAnswerInsideTheConfiguredTimeout_ReportsATimeout()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(4);
        var clock = new FakeTimeProvider();
        var entered = new TaskCompletionSource();
        using var collector = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();

            var blocked = new TaskCompletionSource();
            await using var registration = cancellationToken.Register(
                () => blocked.TrySetCanceled(cancellationToken));
            await blocked.Task;

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var forwarder = ForwarderOver(
            collector,
            OtlpExportProtocol.HttpProtobuf,
            headers: null,
            timeout: timeout,
            clock: clock);

        // Act
        var forwarding = forwarder.ForwardAsync(
            ClientTelemetrySignal.Traces,
            Batch,
            TestContext.Current.CancellationToken);

        await entered.Task;
        clock.Advance(timeout);

        // Assert
        Assert.Equal(ClientTelemetryFailure.TimedOut, (await forwarding).Failure);
    }

    /// <summary>The control for the deadline above: a client that hung up is a different condition from a collector that did not answer.</summary>
    /// <remarks>
    /// Both arrive as the same exception out of the same linked source, so what tells them apart is which token fired.
    /// Asserting only the timeout would leave a guard that reported a disconnected browser tab as a slow collector
    /// passing, which is the one distinction the counter and the line an operator reads are built on.
    /// </remarks>
    [Fact]
    public async Task ForwardAsync_ACallerThatCancelledWhileTheDestinationWasStillPending_ReportsCancellation()
    {
        // Arrange
        var entered = new TaskCompletionSource();
        using var collector = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();

            var blocked = new TaskCompletionSource();
            await using var registration = cancellationToken.Register(
                () => blocked.TrySetCanceled(cancellationToken));
            await blocked.Task;

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var forwarder = ForwarderOver(
            collector,
            OtlpExportProtocol.HttpProtobuf,
            headers: null,
            timeout: TimeSpan.FromSeconds(4),
            clock: new FakeTimeProvider());
        using var caller = new CancellationTokenSource();

        // Act
        var forwarding = forwarder.ForwardAsync(ClientTelemetrySignal.Traces, Batch, caller.Token);

        await entered.Task;
        await caller.CancelAsync();

        // Assert
        Assert.Equal(ClientTelemetryFailure.Cancelled, (await forwarding).Failure);
    }

    /// <summary>Composes one gRPC answer: the status in the trailers, and the message inside its frame.</summary>
    private static HttpResponseMessage GrpcAnswer(int status, byte[] message)
    {
        byte[] framed = message.Length == 0 ? [] : [0, 0, 0, 0, (byte)message.Length, .. message];
        var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(framed) };
        answer.TrailingHeaders.TryAddWithoutValidation(
            "grpc-status",
            status.ToString(CultureInfo.InvariantCulture));

        return answer;
    }

    /// <summary>Builds the forwarder over one collector, through a factory that hands out a client per call.</summary>
    /// <remarks>
    /// A callback rather than a fixed instance, because the forwarder disposes the client it opened — which is what
    /// opening one per operation means — and a substitute answering the same instance twice would hand the second call
    /// a disposed one.
    /// </remarks>
    private static ClientTelemetryForwarder ForwarderOver(
        HttpMessageHandler collector,
        OtlpExportProtocol protocol,
        IReadOnlyList<KeyValuePair<string, string>>? headers = null,
        TimeSpan? timeout = null,
        TimeProvider? clock = null)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(ClientTelemetryForwarder.HttpClientName)
            .Returns(_ => new HttpClient(collector, disposeHandler: false));

        var destination = new ClientTelemetryDestination(
            new Uri("https://collector.example.test"),
            protocol,
            headers ?? [],
            timeout ?? TimeSpan.FromSeconds(10));

        return new ClientTelemetryForwarder(clients, destination, clock ?? TimeProvider.System);
    }
}
