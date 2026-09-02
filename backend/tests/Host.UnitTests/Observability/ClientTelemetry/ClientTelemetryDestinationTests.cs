// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability.ClientTelemetry;
using OpenTelemetry.Exporter;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability.ClientTelemetry;

/// <summary>Covers where a forwarded batch is addressed and what it carries with it.</summary>
/// <remarks>
/// The settings are stated rather than read from the process environment, which is what the exporter's own constructor
/// does: a test that let the ambient environment decide would pass or fail on whatever the machine running it exports
/// to. What is asserted here is the composition of an address and the reading of a header list, which is all this type
/// decides.
/// </remarks>
public sealed class ClientTelemetryDestinationTests
{
    /// <summary>The path is the specification's, so a collector serving OTLP over HTTP is reached where it listens.</summary>
    [Fact]
    public void AddressFor_OverHttp_AppendsThePathTheSpecificationFixesForTheSignal()
    {
        // Arrange
        var destination = DestinationAt("https://collector.example.test:4318", OtlpExportProtocol.HttpProtobuf);

        // Act
        var addresses = ClientTelemetrySignal.All.Select(destination.AddressFor).ToArray();

        // Assert
        Assert.Equal(
            [
                new Uri("https://collector.example.test:4318/v1/traces"),
                new Uri("https://collector.example.test:4318/v1/metrics"),
                new Uri("https://collector.example.test:4318/v1/logs"),
            ],
            addresses);
    }

    /// <summary>A gRPC endpoint names a server, so the service and the method are what make it an export.</summary>
    [Fact]
    public void AddressFor_OverGrpc_AppendsTheSignalsOwnServiceMethod()
    {
        // Arrange
        var destination = DestinationAt("http://localhost:4317", OtlpExportProtocol.Grpc);

        // Act
        var address = destination.AddressFor(ClientTelemetrySignal.Traces);

        // Assert
        Assert.Equal(
            new Uri("http://localhost:4317/opentelemetry.proto.collector.trace.v1.TraceService/Export"),
            address);
    }

    /// <summary>An endpoint an operator wrote with a trailing slash addresses the same collector as one without.</summary>
    [Fact]
    public void AddressFor_AnEndpointWrittenWithATrailingSlash_ComposesTheSameAddress()
    {
        // Arrange
        var destination = DestinationAt("https://collector.example.test/otlp/", OtlpExportProtocol.HttpProtobuf);

        // Act
        var address = destination.AddressFor(ClientTelemetrySignal.Logs);

        // Assert
        Assert.Equal(new Uri("https://collector.example.test/otlp/v1/logs"), address);
    }

    /// <summary>The collector's credential travels in this list, so a pair the exporter would send is a pair this sends.</summary>
    [Fact]
    public void From_HeadersInTheStandardForm_ReadsEachPairWithItsValueDecoded()
    {
        // Arrange
        var exporterSettings = new OtlpExporterOptions
        {
            Endpoint = new Uri("https://collector.example.test"),
            Headers = "api-key=abc123, x-scope=team%2Cmail",
        };

        // Act
        var destination = ClientTelemetryDestination.From(exporterSettings);

        // Assert
        Assert.Equal(
            [
                new KeyValuePair<string, string>("api-key", "abc123"),
                new KeyValuePair<string, string>("x-scope", "team,mail"),
            ],
            destination.Headers);
    }

    /// <summary>The control for the reading above, which would otherwise pass over a parser that found nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nothing-that-is-a-pair")]
    public void From_NoHeaderPairs_ReadsNone(string? headers)
    {
        // Arrange
        var exporterSettings = new OtlpExporterOptions
        {
            Endpoint = new Uri("https://collector.example.test"),
            Headers = headers,
        };

        // Act
        var destination = ClientTelemetryDestination.From(exporterSettings);

        // Assert
        Assert.Empty(destination.Headers);
    }

    /// <summary>The ceiling on one forward is the exporter's own, so both move together.</summary>
    [Fact]
    public void From_TheExportersConfiguredTimeout_IsWhatOneForwardRunsUnder()
    {
        // Arrange
        var exporterSettings = new OtlpExporterOptions
        {
            Endpoint = new Uri("https://collector.example.test"),
            TimeoutMilliseconds = 2500,
        };

        // Act
        var destination = ClientTelemetryDestination.From(exporterSettings);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(2500), destination.Timeout);
    }

    private static ClientTelemetryDestination DestinationAt(string endpoint, OtlpExportProtocol protocol) =>
        new(new Uri(endpoint), protocol, [], TimeSpan.FromSeconds(10));
}
