// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>One of the three OTLP signals the client endpoint accepts and forwards.</summary>
/// <remarks>
/// <para>
/// A closed set of three rather than an enumeration, because each member carries strings the proxy composes a request
/// from: the path the OTLP specification fixes for it, and the gRPC method the same payload is exported to when the
/// deployment's own exporter speaks that protocol. Neither is derivable from an ordinal, and both are wire contracts
/// this repository does not get to choose.
/// </para>
/// <para>
/// The three request messages are shaped identically for everything the proxy does with them — a repeated resource
/// envelope at field 1, a resource at field 1 within it, repeated scopes at field 2, and repeated records at field 2
/// within those — so nothing here says how a payload is read. <see cref="OtlpExportPayload" /> rewrites all three the
/// same way and is told which signal it holds for one purpose alone: a metric's measurements sit one level below the
/// metric, so counting them against the batch bound is the one thing that is not the same in all three. That is still
/// a decision the walker takes from the member rather than from a parser here, which is why a signal is a name and two
/// strings.
/// </para>
/// </remarks>
internal sealed record ClientTelemetrySignal
{
    private ClientTelemetrySignal(string name, string route, string serviceMethod)
    {
        this.Name = name;
        this.Route = route;
        this.ServiceMethod = serviceMethod;
    }

    /// <summary>Gets the traces signal.</summary>
    public static ClientTelemetrySignal Traces { get; } = new(
        "traces",
        "/v1/traces",
        "/opentelemetry.proto.collector.trace.v1.TraceService/Export");

    /// <summary>Gets the metrics signal.</summary>
    public static ClientTelemetrySignal Metrics { get; } = new(
        "metrics",
        "/v1/metrics",
        "/opentelemetry.proto.collector.metrics.v1.MetricsService/Export");

    /// <summary>Gets the logs signal.</summary>
    public static ClientTelemetrySignal Logs { get; } = new(
        "logs",
        "/v1/logs",
        "/opentelemetry.proto.collector.logs.v1.LogsService/Export");

    /// <summary>Gets the three signals, in the order the proxy publishes them.</summary>
    public static IReadOnlyList<ClientTelemetrySignal> All { get; } = [Traces, Metrics, Logs];

    /// <summary>Gets the word this signal is written with wherever the proxy reports one of them apart from the others.</summary>
    public string Name { get; }

    /// <summary>Gets the path the OTLP specification fixes for this signal, beneath whatever prefix serves it.</summary>
    public string Route { get; }

    /// <summary>Gets the gRPC method the same payload is exported to where the deployment's exporter speaks that protocol.</summary>
    public string ServiceMethod { get; }
}
