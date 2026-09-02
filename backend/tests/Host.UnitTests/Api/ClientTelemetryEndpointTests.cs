// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OpenTelemetry.Exporter;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the OTLP routes accept, what they refuse, and what leaves under whose name.</summary>
/// <remarks>
/// The forwarder is real here rather than substituted, over the suite's HTTP double, because the claim worth asserting
/// is what arrived at the collector: that the batch carries the owner this deployment authenticated and not the one the
/// client claimed. A substitute between the handler and the wire would let the attribution be asserted against the
/// argument the test itself arranged.
/// </remarks>
public sealed class ClientTelemetryEndpointTests
{
    private static readonly MailOwnerId AuthenticatedOwner =
        MailOwnerId.Create(new Guid("9f2a1c64-0000-4000-8000-000000000001"));

    /// <summary>A deployment that named no collector serves nothing, which is what "the endpoint is off" looks like here.</summary>
    [Fact]
    public void MapClientTelemetry_WithNoDestinationConfigured_MapsNoRoute()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(ClientEndpointOptions.RoutePrefix).MapClientTelemetry();

        // Assert
        Assert.Empty(endpoints.Materialize());
    }

    /// <summary>The paths are the specification's, so a client points its exporter at the prefix and appends nothing.</summary>
    [Fact]
    public void MapClientTelemetry_WithADestinationConfigured_ServesTheThreeSignalPathsBeneathTheTelemetryPrefix()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapGroup(ClientEndpointOptions.RoutePrefix).MapClientTelemetry();

        // Assert
        Assert.Equal(
            [
                $"{ClientEndpointOptions.RoutePrefix}/telemetry/v1/logs",
                $"{ClientEndpointOptions.RoutePrefix}/telemetry/v1/metrics",
                $"{ClientEndpointOptions.RoutePrefix}/telemetry/v1/traces",
            ],
            endpoints.Materialize()
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText)
                .Order(StringComparer.Ordinal));
    }

    /// <summary>The claim the whole feature rests on, asserted at the wire rather than at the argument.</summary>
    [Fact]
    public async Task AcceptAsync_ABatchClaimingAnOwnerOfItsOwn_ForwardsItNamingTheAuthenticatedOne()
    {
        // Arrange
        using var collector = AcceptingCollector();
        var request = Requesting(OtlpExportRequests.Batch(
            [new KeyValuePair<string, string>(ClientTelemetryEndpoint.OwnerTagName, "somebody-else")],
            records: 3));

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, answered);
        var forwarded = Assert.Single(collector.RecordedRequests).Content.ToArray();
        Assert.Equal(
            [new KeyValuePair<string, string>(ClientTelemetryEndpoint.OwnerTagName, AuthenticatedOwner.ToString())],
            OtlpExportRequests.ResourceAttributes(forwarded));
    }

    /// <summary>One encoding, one parser, one bound: anything else is refused with the status that says so.</summary>
    [Fact]
    public async Task AcceptAsync_ABodyThatIsNotProtocolBuffers_RefusesWithoutForwardingAnything()
    {
        // Arrange
        using var collector = AcceptingCollector();
        var request = Requesting(OtlpExportRequests.Batch([], records: 1));
        request.Request.ContentType = "application/json";

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, answered);
        Assert.Empty(collector.RecordedRequests);
    }

    /// <summary>A batch this endpoint cannot read is one it must not pass on to somebody's collector.</summary>
    [Fact]
    public async Task AcceptAsync_ABodyThatIsNotAnExportRequest_RefusesWithoutForwardingAnything()
    {
        // Arrange
        using var collector = AcceptingCollector();
        var request = Requesting([0xFF, 0xFF, 0xFF]);

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, answered);
        Assert.Empty(collector.RecordedRequests);
    }

    /// <summary>The rate bound, and the answer that tells a client to hold rather than to stop.</summary>
    [Fact]
    public async Task AcceptAsync_PastTheOwnersRate_RefusesWithTooManyRequestsAndSaysHowLongToHold()
    {
        // Arrange
        using var collector = AcceptingCollector();
        using var quota = new ClientTelemetryQuota();

        foreach (var _ in Enumerable.Range(0, ClientTelemetryQuota.BurstCapacity))
        {
            quota.TryAdmit(AuthenticatedOwner.ToString());
        }

        var request = Requesting(OtlpExportRequests.Batch([], records: 1));

        // Act
        var answered = await AcceptAsync(request, collector, quota);

        // Assert
        Assert.Equal(StatusCodes.Status429TooManyRequests, answered);
        Assert.Equal("60", request.Response.Headers.RetryAfter.ToString());
        Assert.Empty(collector.RecordedRequests);
    }

    /// <summary>A batch that did not arrive is answered as retryable, because the client is the one holding it.</summary>
    [Fact]
    public async Task AcceptAsync_ACollectorThatDidNotTakeTheBatch_AnswersServiceUnavailable()
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new ByteArrayContent([]) });
        var request = Requesting(OtlpExportRequests.Batch([], records: 1));

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, answered);
    }

    /// <summary>A partial success reaches the client, because a bare success would hide a rejection it is owed.</summary>
    [Fact]
    public async Task AcceptAsync_ACollectorReportingAPartialSuccess_RelaysItRatherThanAnsweringABareSuccess()
    {
        // Arrange
        byte[] partialSuccess = [0x0A, 0x02, 0x08, 0x03];
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(partialSuccess) });
        var request = Requesting(OtlpExportRequests.Batch([], records: 1));
        var body = new MemoryStream();
        request.Response.Body = body;

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, answered);
        Assert.Equal(partialSuccess, body.ToArray());
        Assert.Equal(ClientTelemetryForwarder.ProtobufMediaType, request.Response.ContentType);
    }

    /// <summary>What a collector says about a batch it would not take is about this deployment, not about the client.</summary>
    [Fact]
    public async Task AcceptAsync_ACollectorThatRefusedTheBatch_AnswersWithoutRelayingWhatItSaid()
    {
        // Arrange
        byte[] collectorReason = [0x0A, 0x02, 0x08, 0x10];
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new ByteArrayContent(collectorReason) });
        var request = Requesting(OtlpExportRequests.Batch([], records: 1));
        var body = new MemoryStream();
        request.Response.Body = body;

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, answered);
        Assert.NotEqual(collectorReason, body.ToArray());
        Assert.NotEmpty(body.ToArray());
    }

    /// <summary>A collector that will not take this deployment's credential is not the client's batch being wrong.</summary>
    /// <remarks>
    /// Answered as retryable rather than as a refusal, because a browser told to stop would drop telemetry over a
    /// header only an operator can correct — and the operator is told which condition it is, on the counter and in the
    /// line the proxy writes, rather than through this answer.
    /// </remarks>
    [Fact]
    public async Task AcceptAsync_ACollectorRefusingThisDeploymentsCredential_TellsTheClientToHold()
    {
        // Arrange
        using var collector = FakeHttpMessageHandler.AlwaysResponding(
            () => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new ByteArrayContent([]) });
        var request = Requesting(OtlpExportRequests.Batch([], records: 1));

        // Act
        var answered = await AcceptAsync(request, collector);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, answered);
    }

    /// <summary>Nobody exports on somebody's behalf without a person to attribute it to.</summary>
    [Fact]
    public async Task AcceptAsync_ACallerActingForNoOwner_RefusesBeforeReadingTheBody()
    {
        // Arrange
        using var collector = AcceptingCollector();
        var request = Requesting(OtlpExportRequests.Batch([], records: 1));

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(async () => await AcceptAsync(
            request,
            collector,
            quota: null,
            authorization: AccessAuthorizations.ForAdministratorGranted()));

        // Assert
        Assert.Empty(collector.RecordedRequests);
    }

    /// <summary>Runs one request through the handler and reports the status it answered with.</summary>
    private static async Task<int> AcceptAsync(
        DefaultHttpContext request,
        FakeHttpMessageHandler collector,
        ClientTelemetryQuota? quota = null,
        AccessAuthorization? authorization = null)
    {
        using var owned = quota is null ? new ClientTelemetryQuota() : null;

        var answered = await ClientTelemetryEndpoint.AcceptAsync(
            ClientTelemetrySignal.Traces,
            request,
            authorization ?? AccessAuthorizations.ForOwnerGranted(AuthenticatedOwner),
            quota ?? owned!,
            ForwarderOver(collector),
            new ClientTelemetryProxyTelemetry(
                new RecordingLogger<ClientTelemetryProxyTelemetry>(),
                new FakeTimeProvider()),
            TestContext.Current.CancellationToken);

        await answered.ExecuteAsync(request);

        return request.Response.StatusCode;
    }

    /// <summary>Composes the request one export arrives as.</summary>
    private static DefaultHttpContext Requesting(byte[] batch)
    {
        var request = new DefaultHttpContext();
        request.Request.Method = HttpMethods.Post;
        request.Request.ContentType = ClientTelemetryForwarder.ProtobufMediaType;
        request.Request.ContentLength = batch.Length;
        request.Request.Body = new MemoryStream(batch);
        request.Response.Body = new MemoryStream();

        return request;
    }

    private static FakeHttpMessageHandler AcceptingCollector() => FakeHttpMessageHandler.AlwaysResponding(
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });

    private static ClientTelemetryForwarder ForwarderOver(HttpMessageHandler collector)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(ClientTelemetryForwarder.HttpClientName)
            .Returns(_ => new HttpClient(collector, disposeHandler: false));

        return new ClientTelemetryForwarder(
            clients,
            new ClientTelemetryDestination(
                new Uri("https://collector.example.test"),
                OtlpExportProtocol.HttpProtobuf,
                [],
                TimeSpan.FromSeconds(10)));
    }

    private static TestEndpointRouteBuilder BuildRouteBuilder()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddSingleton(new ClientTelemetryDestination(
            new Uri("https://collector.example.test"),
            OtlpExportProtocol.HttpProtobuf,
            [],
            TimeSpan.FromSeconds(10)));

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }
}
