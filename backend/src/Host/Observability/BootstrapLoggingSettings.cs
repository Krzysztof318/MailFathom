// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Versioning;

namespace MailFathom.Host.Observability;

/// <summary>Describes the service identity and export destination that the bootstrap logging pipeline is built from.</summary>
/// <param name="ServiceName">The application name reported with every startup record.</param>
/// <param name="ServiceVersion">The semantic version stamped into the host assembly, without source-control build metadata.</param>
/// <param name="ServiceRevision">The source revision the host assembly was built from, or <c>unknown</c> when the build supplied none.</param>
/// <param name="EnvironmentName">The host environment name reported with the startup record.</param>
/// <param name="ExportsToCollector">Whether an OTLP endpoint is configured and the exporter should therefore be registered.</param>
/// <remarks>
/// <para>
/// Every configured value comes from an environment variable rather than from <see cref="IConfiguration" />, because
/// the pipeline these settings build is created before <c>WebApplication.CreateBuilder</c> and therefore before
/// configuration exists. A malformed <c>appsettings.json</c> is one of the failures the pipeline has to report, so it
/// cannot be a prerequisite for it. The environment is also the only source the standalone OTLP exporter itself reads,
/// which keeps the decision to register the exporter and the endpoint it then targets from disagreeing.
/// </para>
/// <para>
/// The version and the revision are the exception, and deliberately not configurable at all: they are read from the
/// assembly's own build-time metadata, so a deployment cannot tell the process to report a build it is not running.
/// </para>
/// </remarks>
internal sealed record BootstrapLoggingSettings(
    string ServiceName,
    string ServiceVersion,
    string ServiceRevision,
    string EnvironmentName,
    bool ExportsToCollector)
{
    private const string ServiceNameVariable = "OTEL_SERVICE_NAME";
    private const string ExporterEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
    private const string DotnetEnvironmentVariable = "DOTNET_ENVIRONMENT";
    private const string DefaultEnvironmentName = "Production";

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
        var stampedVersion = StampedAssemblyVersion.ReadFrom(HostAssembly);

        return new BootstrapLoggingSettings(
            FirstNonBlank(configuredServiceName, HostAssemblyName),
            stampedVersion.Version,
            stampedVersion.Revision,
            FirstNonBlank(configuredEnvironmentName, DefaultEnvironmentName),
            !string.IsNullOrWhiteSpace(readEnvironmentVariable(ExporterEndpointVariable)));
    }

    /// <summary>The assembly whose stamped identity the startup record reports.</summary>
    /// <remarks>
    /// This assembly rather than the entry assembly, so the reported identity is the host's own under every process
    /// that loads it, including the test runner.
    /// </remarks>
    private static Assembly HostAssembly => typeof(BootstrapLoggingSettings).Assembly;

    private static string HostAssemblyName => HostAssembly.GetName().Name ?? nameof(MailFathom);

    private static string FirstNonBlank(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
}
