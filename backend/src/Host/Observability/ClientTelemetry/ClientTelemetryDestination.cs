// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using OpenTelemetry.Exporter;

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>Where the proxy forwards a client's telemetry, which is wherever this process exports its own.</summary>
/// <remarks>
/// <para>
/// There is no second destination and no key of its own. The collector's address and its credential belong to the
/// deployment — they travel in <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> and <c>OTEL_EXPORTER_OTLP_HEADERS</c>, which
/// <see cref="Configuration.EnvironmentOnlySettings" /> already refuses from every source but the process environment —
/// and a client holding either would be publishing them to whoever opened the developer tools. One collector, one
/// credential, both stacks.
/// </para>
/// <para>
/// The values are read through <see cref="OtlpExporterOptions" /> rather than out of the environment a second time.
/// That is the type the SDK's own exporter is configured from, so the endpoint, the protocol, the headers, and the
/// timeout this forwards under are the ones the deployment's own telemetry already leaves by, including every
/// precedence rule about which variable outranks which. A reading of its own would be a second answer to a question
/// with one.
/// </para>
/// <para>
/// It is resolved once, while the host is being composed, because the exporter reads its own configuration then and a
/// destination the proxy re-read per request could come to disagree with where the service's own signals are going.
/// </para>
/// </remarks>
/// <param name="Endpoint">The collector's address, as the exporter resolved it.</param>
/// <param name="Protocol">Which OTLP transport the deployment's exporter speaks, which is the one a batch is forwarded over.</param>
/// <param name="Headers">What the collector requires on a request, which is where its credential travels.</param>
/// <param name="Timeout">The ceiling on one forwarded request.</param>
internal sealed record ClientTelemetryDestination(
    Uri Endpoint,
    OtlpExportProtocol Protocol,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    TimeSpan Timeout)
{
    /// <summary>Reads the destination the deployment's own exporter is configured with.</summary>
    /// <param name="exporterSettings">The exporter settings, which read the standard variables when they are constructed.</param>
    /// <returns>The destination one batch is forwarded to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exporterSettings" /> is <see langword="null" />.</exception>
    internal static ClientTelemetryDestination From(OtlpExporterOptions exporterSettings)
    {
        ArgumentNullException.ThrowIfNull(exporterSettings);

        return new ClientTelemetryDestination(
            exporterSettings.Endpoint,
            exporterSettings.Protocol,
            ParseHeaders(exporterSettings.Headers),
            TimeSpan.FromMilliseconds(exporterSettings.TimeoutMilliseconds));
    }

    /// <summary>Names the address one signal's batch is posted to.</summary>
    /// <param name="signal">The signal being forwarded.</param>
    /// <returns>The address the request is sent to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The signal's own path is appended under both protocols, because under both the address names a server rather
    /// than a signal: over gRPC the method is part of the address, and over HTTP the specification fixes the path a
    /// receiver serves each signal at. What it is appended to is always the base endpoint, because that is the variable
    /// this destination is read from and a deployment that set only the per-signal ones serves no route here at all.
    /// </remarks>
    internal Uri AddressFor(ClientTelemetrySignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        return Append(
            this.Endpoint,
            this.Protocol == OtlpExportProtocol.Grpc ? signal.ServiceMethod : signal.Route);
    }

    /// <summary>Reads the header list the OpenTelemetry specification defines for the standard variable.</summary>
    /// <remarks>
    /// A pair whose value carries a comma or an equals sign arrives percent-encoded, which is the encoding the
    /// specification names and the one the exporter's own reader undoes. A malformed pair is dropped rather than
    /// refused: the exporter reading the same string drops it too, and failing startup here over a value the exporter
    /// accepted would refuse a deployment whose own telemetry works.
    /// </remarks>
    private static IReadOnlyList<KeyValuePair<string, string>> ParseHeaders(string? headers) =>
    [
        .. (headers ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Length > 0)
            .Select(pair => new KeyValuePair<string, string>(
                pair[0].Trim(),
                Uri.UnescapeDataString(pair[1].Trim()))),
    ];

    private static Uri Append(Uri endpoint, string path) =>
        new UriBuilder(endpoint) { Path = endpoint.AbsolutePath.TrimEnd('/') + path }.Uri;
}
