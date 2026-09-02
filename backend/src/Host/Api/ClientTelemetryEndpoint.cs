// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Api;

/// <summary>Takes the client's own telemetry and forwards it to the collector this deployment already exports to.</summary>
/// <remarks>
/// <para>
/// It is the one route family on this surface that is not about mail. A browser head cannot hold the collector's
/// address or its credential — both belong to the deployment, they travel in <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> and
/// <c>OTEL_EXPORTER_OTLP_HEADERS</c>, and a bundle carrying either would be publishing them to whoever opened the
/// developer tools. So the client exports to MailFathom with the token it signed in with, and MailFathom forwards. One
/// collector, one credential, both stacks, and a client span and the service spans it caused end up in one place.
/// </para>
/// <para>
/// <b>Identity is settled here rather than taken from the payload.</b> A client says what it likes about the browser,
/// the release, and the screen, and none of that is this deployment's business; whose telemetry it is, is. The owner
/// comes off the credential that authenticated, and it is written over whatever arrived under that name rather than
/// merged with it — there is no argument and no resource attribute in which a client could name an owner of its own
/// and be believed.
/// </para>
/// <para>
/// The guarantee is about the <b>resource</b>, which is where OpenTelemetry puts the identity of what produced a
/// signal and where a backend reads it from. A client remains free to write anything it likes into a span, a log
/// record, or a metric data point, including a key spelled like this one, exactly as it is free to write anything into
/// the text of a span name — those are the client's own words about its own work rather than an attribution this
/// deployment made, and nothing here reads them. Stripping them would mean walking every record of all three signals
/// against their schemas, which is the decoding this proxy deliberately does not do and which would make it a
/// processor. So the rule to read telemetry by is the resource's own attribution, and it is the one thing in a
/// forwarded batch a client cannot influence.
/// </para>
/// <para>
/// <b>The routes exist only where the deployment named a destination.</b> Enabling the export is the whole switch and it
/// is the variable the service's own exporter already reads, so there is no second key and no way for the two to
/// disagree. A deployment that named none serves nothing here, which a client learns from the same <c>404</c> the rest
/// of the surface answers with where something is not served — a non-retryable answer, which is what stops a client
/// exporting into a deployment that will never forward it.
/// </para>
/// <para>
/// Everything an unauthenticated shape could push through is bounded before anything leaves: the body, the number of
/// records in one batch, and how often one owner may export. Each refusal is answered with the status the OTLP
/// specification names and a status document rather than a truncation, so the client can tell a batch that will never
/// be accepted from one worth holding.
/// </para>
/// </remarks>
internal static class ClientTelemetryEndpoint
{
    /// <summary>The prefix the OTLP routes are served beneath, relative to the client prefix.</summary>
    /// <remarks>
    /// A prefix of its own because the paths under it are not this repository's to choose: the specification fixes
    /// <c>/v1/traces</c> and its two siblings relative to whatever endpoint a client is configured with, so a client
    /// points its exporter at this prefix and appends nothing itself.
    /// </remarks>
    internal const string TelemetryRoutePrefix = "/telemetry";

    /// <summary>The resource attribute naming whose telemetry a forwarded batch is.</summary>
    /// <remarks>The same key the guarded-egress span carries the same identifier under, because one dimension keeps one key wherever it is published.</remarks>
    internal const string OwnerTagName = "mailfathom.owner";

    /// <summary>The largest export body this endpoint reads.</summary>
    /// <remarks>
    /// Far below the ceiling the specification names a receiver must enforce, because the sender here is a browser
    /// exporting every few seconds rather than a collector shipping an aggregation: a batch anywhere near this is a
    /// client that has stopped exporting and started accumulating, and refusing it is the honest answer.
    /// </remarks>
    internal const int MaxRequestBytes = 4 * 1024 * 1024;

    /// <summary>The most records one batch may carry, a record being a span, a log record, or a single metric data point.</summary>
    /// <remarks>
    /// Bounded beside the byte count rather than instead of it, because the two are different costs: the octets are
    /// what this process buffers, and the records are what the destination is charged for. A metric counts as its data
    /// points rather than as one definition for exactly that reason — a collector bills the measurements.
    /// </remarks>
    internal const int MaxRecordsPerBatch = 10_000;

    private const int InvalidArgumentStatus = 3;
    private const int ResourceExhaustedStatus = 8;
    private const int UnavailableStatus = 14;

    /// <summary>Maps the three OTLP routes into the client group, where the deployment named somewhere to forward to.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The destination being registered is what says the deployment configured one, so the switch is read where
    /// composition read it rather than a second time here. Nothing is mapped without it, which is what makes "not
    /// configured" answer as nothing served rather than as a route that always fails.
    /// </remarks>
    internal static void MapClientTelemetry(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        if (((IEndpointRouteBuilder)api).ServiceProvider.GetService<ClientTelemetryDestination>() is null)
        {
            return;
        }

        foreach (var signal in ClientTelemetrySignal.All)
        {
            api.MapPost(
                    $"{TelemetryRoutePrefix}{signal.Route}",
                    (HttpContext context,
                        [FromServices] AccessAuthorization authorization,
                        [FromServices] ClientTelemetryQuota quota,
                        [FromServices] ClientTelemetryForwarder forwarder,
                        [FromServices] ClientTelemetryProxyTelemetry telemetry,
                        CancellationToken cancellationToken) => AcceptAsync(
                        signal,
                        context,
                        authorization,
                        quota,
                        forwarder,
                        telemetry,
                        cancellationToken))

                // The attribute is reached for its metadata rather than as an MVC filter, exactly as the record routes
                // reach it: it implements IRequestSizeLimitMetadata, so a body over the bound is stopped by the request
                // body feature instead of being buffered here first.
                .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
                .Accepts<Stream>(ClientTelemetryForwarder.ProtobufMediaType)
                .Produces<Stream>(StatusCodes.Status200OK, ClientTelemetryForwarder.ProtobufMediaType)
                .RequireNoPermission();
        }
    }

    /// <summary>Accepts one export batch, attributes it, and forwards it.</summary>
    /// <param name="signal">The signal the route serves.</param>
    /// <param name="context">The request being answered, whose body carries the batch.</param>
    /// <param name="authorization">Answers whose telemetry this is, and refuses a caller acting for nobody.</param>
    /// <param name="quota">Bounds how often one owner may export.</param>
    /// <param name="forwarder">Sends the batch to the deployment's own collector.</param>
    /// <param name="telemetry">Reports what was accepted, refused, forwarded, and not forwarded.</param>
    /// <param name="cancellationToken">Cancels the forward when the client disconnects.</param>
    /// <returns>The destination's own answer, or the refusal that stopped the batch, as the specification's own protocol buffers documents.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any resolved dependency is <see langword="null" />.</exception>
    /// <remarks>
    /// The order is deliberate. The credential is resolved to an owner first, because an export nobody can be
    /// attributed to must not be read at all; the quota is spent next, so a client past its rate costs this process one
    /// refusal rather than a parse; and only then is the batch read, bounded, and rewritten.
    /// </remarks>
    internal static async Task<IResult> AcceptAsync(
        ClientTelemetrySignal signal,
        HttpContext context,
        AccessAuthorization authorization,
        ClientTelemetryQuota quota,
        ClientTelemetryForwarder forwarder,
        ClientTelemetryProxyTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(forwarder);
        ArgumentNullException.ThrowIfNull(telemetry);

        var owner = authorization.RequireOwner();

        if (!SpeaksProtobuf(context.Request.ContentType))
        {
            return Refused(
                telemetry,
                signal,
                "unsupported_media_type",
                StatusCodes.Status415UnsupportedMediaType,
                InvalidArgumentStatus,
                $"This endpoint accepts '{ClientTelemetryForwarder.ProtobufMediaType}' alone.");
        }

        if (!quota.TryAdmit(owner.ToString()))
        {
            return Refused(
                telemetry,
                signal,
                "rate_limited",
                StatusCodes.Status429TooManyRequests,
                ResourceExhaustedStatus,
                "This deployment accepts telemetry from one credential at a bounded rate. Hold what has not been exported and send it with the next batch.",
                ClientTelemetryQuota.RetryAfter);
        }

        if (await ReadBatchAsync(context, cancellationToken) is not { } batch)
        {
            return Refused(
                telemetry,
                signal,
                "too_large",
                StatusCodes.Status413PayloadTooLarge,
                InvalidArgumentStatus,
                $"An export batch is at most {MaxRequestBytes} bytes.");
        }

        var rewritten = OtlpExportPayload.Rewrite(
            batch,
            signal,
            OwnerTagName,
            owner.ToString(),
            MaxRecordsPerBatch);

        if (rewritten.Refusal != OtlpPayloadRefusal.None)
        {
            return RefusedPayload(telemetry, signal, rewritten.Refusal);
        }

        telemetry.RecordAccepted(signal, rewritten.RecordCount);

        var forwarding = await forwarder.ForwardAsync(signal, rewritten.Body, cancellationToken);
        telemetry.RecordForwarding(signal, forwarding);

        return Answered(forwarding);
    }

    /// <summary>Answers the client with what the destination said, or with what its silence means for the batch.</summary>
    /// <remarks>
    /// A destination's own answer is relayed where the batch arrived, because a partial success lives in it and a bare
    /// success would hide a rejection the client is entitled to read. A refusal is answered in this endpoint's own
    /// words instead: what a collector says about a batch it would not take is about this deployment's export rather
    /// than about the client, and it is not a document to publish to a browser. Everything transient answers as unavailability,
    /// which the specification defines as retryable — the client holds what it has not exported, and nothing is queued
    /// on this side.
    /// </remarks>
    private static OtlpProtobufResult Answered(ClientTelemetryForwarding forwarding) => forwarding.Failure switch
    {
        ClientTelemetryFailure.None => new OtlpProtobufResult(StatusCodes.Status200OK, forwarding.Body),
        ClientTelemetryFailure.Refused => new OtlpProtobufResult(
            StatusCodes.Status400BadRequest,
            OtlpExportPayload.Status(InvalidArgumentStatus, "The configured destination refused the batch.")),
        ClientTelemetryFailure.Throttled => new OtlpProtobufResult(
            StatusCodes.Status429TooManyRequests,
            OtlpExportPayload.Status(ResourceExhaustedStatus, "The configured destination is being sent too much."),
            forwarding.RetryAfter),
        _ => new OtlpProtobufResult(
            StatusCodes.Status503ServiceUnavailable,
            OtlpExportPayload.Status(UnavailableStatus, "The configured destination did not take the batch.")),
    };

    /// <summary>Answers a batch this endpoint would not read, counting it and saying why.</summary>
    private static OtlpProtobufResult RefusedPayload(
        ClientTelemetryProxyTelemetry telemetry,
        ClientTelemetrySignal signal,
        OtlpPayloadRefusal refusal) => refusal == OtlpPayloadRefusal.TooManyRecords
        ? Refused(
            telemetry,
            signal,
            "too_many_records",
            StatusCodes.Status413PayloadTooLarge,
            InvalidArgumentStatus,
            $"An export batch carries at most {MaxRecordsPerBatch} records.")
        : Refused(
            telemetry,
            signal,
            "malformed",
            StatusCodes.Status400BadRequest,
            InvalidArgumentStatus,
            "The body is not an OTLP export request this endpoint can read.");

    /// <summary>Counts one refusal and writes the answer that goes with it.</summary>
    /// <remarks>The refusal word and the status travel together so a counter and an answer cannot come to disagree about what happened.</remarks>
    private static OtlpProtobufResult Refused(
        ClientTelemetryProxyTelemetry telemetry,
        ClientTelemetrySignal signal,
        string refusal,
        int httpStatus,
        int statusCode,
        string message,
        TimeSpan? retryAfter = null)
    {
        telemetry.RecordRefused(signal, refusal);

        return new OtlpProtobufResult(httpStatus, OtlpExportPayload.Status(statusCode, message), retryAfter);
    }

    /// <summary>Reads the batch out of the request body, or reports that it is past what this endpoint accepts.</summary>
    /// <remarks>
    /// The bound is enforced twice by construction rather than by care: the request size metadata stops a declared
    /// length before the handler runs and raises during the read where the sender declared none, and the length is read
    /// back afterwards so nothing depends on which of the two fired.
    /// </remarks>
    private static async Task<byte[]?> ReadBatchAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxRequestBytes)
        {
            return null;
        }

        using var batch = new MemoryStream(
            context.Request.ContentLength is { } declared and > 0 and <= MaxRequestBytes ? (int)declared : 0);

        try
        {
            await context.Request.Body.CopyToAsync(batch, cancellationToken);
        }
        catch (BadHttpRequestException)
        {
            return null;
        }

        return batch.Length > MaxRequestBytes ? null : batch.ToArray();
    }

    /// <summary>Reports whether the request declares the one media type this endpoint reads.</summary>
    /// <remarks>JSON is deliberately not accepted. The specification permits a receiver to take either, and one encoding is one parser, one bound, and one thing to get right.</remarks>
    private static bool SpeaksProtobuf(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var declared)
        && string.Equals(
            declared.MediaType.Value,
            ClientTelemetryForwarder.ProtobufMediaType,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes one protocol buffers document back to the client, under the status the specification names.</summary>
    /// <param name="StatusCode">The response status.</param>
    /// <param name="Body">The encoded document, which is empty for a destination that answered nothing.</param>
    /// <param name="RetryAfter">How long the client is asked to hold for, where there is an answer to that.</param>
    /// <remarks>
    /// A result of its own rather than one of the typed helpers, because every one of them fixes the status, the media
    /// type, or both, and this endpoint answers four statuses with the same media type on all of them — the
    /// specification requires the document whether the news is good or not.
    /// </remarks>
    private sealed record OtlpProtobufResult(int StatusCode, byte[] Body, TimeSpan? RetryAfter = null) : IResult
    {
        /// <inheritdoc />
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = this.StatusCode;
            httpContext.Response.ContentType = ClientTelemetryForwarder.ProtobufMediaType;
            httpContext.Response.ContentLength = this.Body.Length;

            if (this.RetryAfter is { } delay)
            {
                httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            }

            return httpContext.Response.Body.WriteAsync(this.Body, 0, this.Body.Length, httpContext.RequestAborted);
        }
    }
}
