// Copyright © 2026 Krzysztof Kasprowicz

using System.Reflection;

namespace MailMcp.Host.Observability;

/// <summary>Describes the service identity and export destination that the bootstrap logging pipeline is built from.</summary>
/// <param name="ServiceName">The application name reported with every startup record.</param>
/// <param name="ServiceVersion">The informational version of the host assembly, without source-control build metadata.</param>
/// <param name="EnvironmentName">The host environment name reported with the startup record.</param>
/// <param name="ExportsToCollector">Whether an OTLP endpoint is configured and the exporter should therefore be registered.</param>
/// <remarks>
/// Every value comes from an environment variable rather than from <see cref="IConfiguration" />, because the pipeline
/// these settings build is created before <c>WebApplication.CreateBuilder</c> and therefore before configuration
/// exists. A malformed <c>appsettings.json</c> is one of the failures the pipeline has to report, so it cannot be a
/// prerequisite for it. The environment is also the only source the standalone OTLP exporter itself reads, which keeps
/// the decision to register the exporter and the endpoint it then targets from disagreeing.
/// </remarks>
internal sealed record BootstrapLoggingSettings(
    string ServiceName,
    string ServiceVersion,
    string EnvironmentName,
    bool ExportsToCollector)
{
    private const string ServiceNameVariable = "OTEL_SERVICE_NAME";
    private const string ExporterEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
    private const string DotnetEnvironmentVariable = "DOTNET_ENVIRONMENT";
    private const string DefaultEnvironmentName = "Production";
    private const string UnknownServiceVersion = "unknown";

    /// <summary>Derives the bootstrap logging settings from the process environment.</summary>
    /// <returns>The settings the bootstrap logging pipeline is composed from.</returns>
    public static BootstrapLoggingSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    /// <summary>Derives the bootstrap logging settings from an arbitrary environment-variable source.</summary>
    /// <param name="readEnvironmentVariable">Reads one environment variable by name, returning <see langword="null" /> when it is unset.</param>
    /// <returns>The settings the bootstrap logging pipeline is composed from.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="readEnvironmentVariable" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The environment name falls back the way the host itself falls back, so a startup record names the environment
    /// the host is about to select rather than a second opinion about it.
    /// </remarks>
    internal static BootstrapLoggingSettings From(Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        var configuredServiceName = readEnvironmentVariable(ServiceNameVariable);
        var configuredEnvironmentName = readEnvironmentVariable(AspNetCoreEnvironmentVariable)
            ?? readEnvironmentVariable(DotnetEnvironmentVariable);

        return new BootstrapLoggingSettings(
            FirstNonBlank(configuredServiceName, HostAssemblyName),
            ReadHostAssemblyVersion(),
            FirstNonBlank(configuredEnvironmentName, DefaultEnvironmentName),
            !string.IsNullOrWhiteSpace(readEnvironmentVariable(ExporterEndpointVariable)));
    }

    private static string HostAssemblyName =>
        typeof(BootstrapLoggingSettings).Assembly.GetName().Name ?? nameof(MailMcp);

    private static string FirstNonBlank(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;

    /// <summary>Reads the informational version of the host assembly, stripped of source-control build metadata.</summary>
    /// <returns>The host assembly version, or <c>unknown</c> when the assembly carries no informational version.</returns>
    /// <remarks>
    /// The attribute is read from this assembly rather than from the entry assembly, so the reported version is the
    /// host's own under every process that loads it, including the test runner. SourceLink appends the commit hash
    /// after a plus sign, which belongs in build provenance rather than in a value an operator groups by.
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
