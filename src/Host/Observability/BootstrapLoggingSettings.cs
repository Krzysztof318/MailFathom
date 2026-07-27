// Copyright © 2026 Krzysztof Kasprowicz

using System.Reflection;

namespace MailMcp.Host.Observability;

/// <summary>Describes the service identity and export destination that the bootstrap logging pipeline is built from.</summary>
/// <param name="ServiceName">The service name reported as an OpenTelemetry resource attribute.</param>
/// <param name="ServiceVersion">The informational version of the host assembly, without source-control build metadata.</param>
/// <param name="EnvironmentName">The host environment name reported with the startup record.</param>
/// <param name="ExportsToCollector">Whether an OTLP endpoint is configured and the exporter should therefore be registered.</param>
internal sealed record BootstrapLoggingSettings(
    string ServiceName,
    string ServiceVersion,
    string EnvironmentName,
    bool ExportsToCollector)
{
    private const string ServiceNameKey = "OTEL_SERVICE_NAME";
    private const string ExporterEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string UnknownServiceVersion = "unknown";

    /// <summary>Derives the bootstrap logging settings from the configuration and environment of the starting host.</summary>
    /// <param name="configuration">The configuration of the host being composed.</param>
    /// <param name="environment">The environment of the host being composed.</param>
    /// <returns>The settings the bootstrap logging pipeline is composed from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> or <paramref name="environment" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The configured service name wins over the application name so that bootstrap records and the records the host's
    /// own pipeline emits after <c>builder.Build()</c> carry one identity. An orchestrator such as Aspire names the
    /// resource it launches through <c>OTEL_SERVICE_NAME</c>, and preferring the assembly name over it would split one
    /// process into two services in the collector.
    /// </remarks>
    public static BootstrapLoggingSettings From(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredServiceName = configuration[ServiceNameKey];
        var serviceName = string.IsNullOrWhiteSpace(configuredServiceName)
            ? environment.ApplicationName
            : configuredServiceName;

        return new BootstrapLoggingSettings(
            serviceName,
            ReadHostAssemblyVersion(),
            environment.EnvironmentName,
            !string.IsNullOrWhiteSpace(configuration[ExporterEndpointKey]));
    }

    /// <summary>Reads the informational version of the host assembly, stripped of source-control build metadata.</summary>
    /// <returns>The host assembly version, or <c>unknown</c> when the assembly carries no informational version.</returns>
    /// <remarks>
    /// The attribute is read from this assembly rather than from the entry assembly, so the reported version is the
    /// host's own under every process that loads it, including the test runner. SourceLink appends the commit hash
    /// after a plus sign, which belongs in build provenance rather than in a resource attribute an operator groups by.
    /// </remarks>
    private static string ReadHostAssemblyVersion()
    {
        var informationalVersion = typeof(BootstrapLoggingSettings).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return UnknownServiceVersion;
        }

        var buildMetadataStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);

        return buildMetadataStart < 0
            ? informationalVersion
            : informationalVersion[..buildMetadataStart];
    }
}
