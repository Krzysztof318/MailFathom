// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using OpenTelemetry.Exporter;

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>Sends one batch of a client's telemetry on to the collector this deployment already exports to.</summary>
/// <remarks>
/// <para>
/// It speaks whichever OTLP transport the deployment's own exporter speaks, because the destination is the same
/// destination and a collector configured for one of them is not necessarily listening for the other. Over HTTP that is
/// the batch posted verbatim under the specification's media type; over gRPC it is the same octets inside the
/// length-prefixed frame that protocol wraps a unary message in, which is the whole of the difference — the message on
/// the wire is identical, so nothing here decodes or re-encodes a payload to change protocol.
/// </para>
/// <para>
/// Nothing is buffered and nothing is retried in this process beyond what the shared resilience handler already does.
/// A batch that cannot be forwarded is dropped and the caller is told so, because the client is the one holding what it
/// has not exported and it will send it again — a queue here would be a second copy of somebody's telemetry, kept in a
/// process that never promised to keep it.
/// </para>
/// <para>
/// A rejection and a failure are answered differently, which is the distinction the OTLP specification draws between a
/// request a receiver will never accept and one it could accept later. A collector refusing the payload is relayed as
/// the refusal it is, so the client stops rather than retrying forever; anything transient is answered as
/// unavailability, which is what tells the client to hold.
/// </para>
/// </remarks>
internal sealed class ClientTelemetryForwarder
{
    /// <summary>The name the forwarding client is registered under, which is the only way to obtain one with these bounds.</summary>
    internal const string HttpClientName = "MailFathom.ClientTelemetryProxy";

    /// <summary>The media type the OTLP specification fixes for a protocol buffers payload over HTTP.</summary>
    internal const string ProtobufMediaType = "application/x-protobuf";

    private const string GrpcMediaType = "application/grpc";
    private const string GrpcStatusHeader = "grpc-status";
    private const int GrpcFrameHeaderLength = 5;

    private readonly IHttpClientFactory clients;
    private readonly ClientTelemetryDestination destination;

    /// <summary>Initializes the forwarder over the destination composition resolved.</summary>
    /// <param name="clients">Builds the bounded client one forward is sent on.</param>
    /// <param name="destination">Where the deployment's own telemetry goes, which is where this goes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clients" /> or <paramref name="destination" /> is <see langword="null" />.</exception>
    public ClientTelemetryForwarder(IHttpClientFactory clients, ClientTelemetryDestination destination)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(destination);

        this.clients = clients;
        this.destination = destination;
    }

    /// <summary>Forwards one batch and reports what the caller should be answered.</summary>
    /// <param name="signal">The signal the batch belongs to.</param>
    /// <param name="batch">The batch, already rewritten to name the owner it belongs to.</param>
    /// <param name="cancellationToken">Cancels the forward when the client disconnects.</param>
    /// <returns>What to answer the client, and the condition to report where the batch did not arrive.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> or <paramref name="batch" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The ceiling is the exporter's own configured timeout, applied here rather than left to the client's registration
    /// so that a deployment changing <c>OTEL_EXPORTER_OTLP_TIMEOUT</c> moves both this and its own exports together.
    /// </remarks>
    internal async Task<ClientTelemetryForwarding> ForwardAsync(
        ClientTelemetrySignal signal,
        byte[] batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(batch);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(this.destination.Timeout);

        try
        {
            using var client = this.clients.CreateClient(HttpClientName);
            using var request = this.Compose(signal, batch);
            using var response = await client.SendAsync(request, deadline.Token);

            return this.destination.Protocol == OtlpExportProtocol.Grpc
                ? await ReadGrpcAsync(response, deadline.Token)
                : await ReadHttpAsync(response, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return ClientTelemetryForwarding.Failed(ClientTelemetryFailure.TimedOut);
        }
        catch (HttpRequestException)
        {
            return ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unreachable);
        }
    }

    /// <summary>Composes the request one batch travels in, under whichever transport the destination speaks.</summary>
    private HttpRequestMessage Compose(ClientTelemetrySignal signal, byte[] batch)
    {
        var overGrpc = this.destination.Protocol == OtlpExportProtocol.Grpc;

        var request = new HttpRequestMessage(HttpMethod.Post, this.destination.AddressFor(signal))
        {
            Content = new ByteArrayContent(overGrpc ? GrpcFrame(batch) : batch),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(overGrpc ? GrpcMediaType : ProtobufMediaType);

        if (overGrpc)
        {
            // gRPC is defined over HTTP/2 alone, and an exact version is what makes a clear-text destination negotiate
            // it rather than fall back to HTTP/1.1 and answer a protocol error the collector never saw.
            request.Version = HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            request.Headers.TryAddWithoutValidation("grpc-encoding", "identity");
            request.Headers.TryAddWithoutValidation("te", "trailers");
        }

        foreach (var header in this.destination.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    /// <summary>Reads what the collector answered an OTLP/HTTP export with.</summary>
    /// <remarks>
    /// A success is relayed body and all, because that body is where a partial success lives: a collector that accepted
    /// eight records of ten says so in it, and answering the client a bare success instead would hide a rejection it is
    /// entitled to read.
    /// </remarks>
    private static async Task<ClientTelemetryForwarding> ReadHttpAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return ClientTelemetryForwarding.Forwarded(
                await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests)
        {
            return ClientTelemetryForwarding.Throttled(response.Headers.RetryAfter?.Delta);
        }

        return (int)response.StatusCode >= StatusCodes.Status500InternalServerError
            ? ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unavailable)
            : ClientTelemetryForwarding.Refused();
    }

    /// <summary>Reads what the collector answered a gRPC export with, which is a status rather than a response code.</summary>
    /// <remarks>
    /// The status arrives in the trailers of an ordinary answer and in the headers of a trailers-only one, so both are
    /// consulted; an answer carrying neither is treated as a failure rather than as a success, because a success is the
    /// one thing a gRPC server states explicitly.
    /// </remarks>
    private static async Task<ClientTelemetryForwarding> ReadGrpcAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (GrpcStatusOf(response) is not { } status)
        {
            return ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unavailable);
        }

        return status switch
        {
            GrpcStatus.Ok => ClientTelemetryForwarding.Forwarded(Unframe(body)),
            GrpcStatus.InvalidArgument or GrpcStatus.NotFound or GrpcStatus.Unimplemented =>
                ClientTelemetryForwarding.Refused(),
            GrpcStatus.ResourceExhausted => ClientTelemetryForwarding.Throttled(retryAfter: null),
            _ => ClientTelemetryForwarding.Failed(ClientTelemetryFailure.Unavailable),
        };
    }

    /// <summary>Reads the gRPC status out of whichever half of the answer carries it.</summary>
    private static int? GrpcStatusOf(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var stated = Stated(response.TrailingHeaders) ?? Stated(response.Headers);

        return int.TryParse(stated, NumberStyles.Integer, CultureInfo.InvariantCulture, out var status)
            ? status
            : null;

        static string? Stated(HttpHeaders headers) =>
            headers.TryGetValues(GrpcStatusHeader, out var values) ? values.FirstOrDefault() : null;
    }

    /// <summary>Wraps one message in the frame a gRPC unary call carries it in.</summary>
    /// <remarks>The leading octet says the message is not compressed, and the four after it are its length, most significant octet first.</remarks>
    private static byte[] GrpcFrame(byte[] message)
    {
        var framed = new byte[GrpcFrameHeaderLength + message.Length];
        framed[0] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1, 4), (uint)message.Length);
        message.CopyTo(framed, GrpcFrameHeaderLength);

        return framed;
    }

    /// <summary>Takes one message back out of the frame a gRPC answer carries it in.</summary>
    /// <remarks>A compressed frame is answered as an empty success rather than relayed, because the client is owed the specification's own encoding and this endpoint asked for none.</remarks>
    private static byte[] Unframe(byte[] framed) =>
        framed.Length > GrpcFrameHeaderLength && framed[0] == 0 ? framed[GrpcFrameHeaderLength..] : [];

    /// <summary>The gRPC status codes this forwarder tells apart, which is every one it answers differently.</summary>
    private static class GrpcStatus
    {
        internal const int Ok = 0;
        internal const int InvalidArgument = 3;
        internal const int NotFound = 5;
        internal const int ResourceExhausted = 8;
        internal const int Unimplemented = 12;
    }
}
