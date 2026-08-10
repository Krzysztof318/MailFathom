// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace MailFathom.Host.Observability;

/// <summary>
/// Reports the lifetime of the host process through a logging pipeline that exists before the dependency-injection
/// container is built and is released only when the process is leaving.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline that <see cref="ServiceDefaultsExtensions.ConfigureOpenTelemetry{TBuilder}" /> registers exists only
/// once <c>builder.Build()</c> has returned, so a failure during composition has nowhere to be written, and its OTLP
/// exporter batches records for five seconds by default. When host startup throws, nothing disposes the application,
/// and the runtime does not guarantee that <c>finally</c> blocks run for an unhandled exception, so the record that
/// explains the crash would never leave the process. Every record written here is therefore exported synchronously,
/// which makes delivery independent of process teardown; a handful of records per process make the cost irrelevant.
/// </para>
/// <para>
/// The pipeline is composed before <c>WebApplication.CreateBuilder</c>, which is where a malformed
/// <c>appsettings.json</c> or a failing configuration provider throws. That places configuration loading inside the
/// reported window and is why <see cref="BootstrapLoggingSettings" /> reads the environment rather than
/// <see cref="IConfiguration" />: the pipeline cannot depend on the thing whose failure it exists to report.
/// </para>
/// <para>
/// The instance takes ownership of the <see cref="ILoggerFactory" /> it is constructed with and disposes it. It is
/// deliberately absent from the container: it has to be usable before the container exists, and a container that
/// disposed it would take the pipeline away exactly when a shutdown failure needs reporting.
/// </para>
/// </remarks>
internal sealed partial class BootstrapLogger : IDisposable
{
    private const string LogCategory = "MailFathom.Host.Startup";

    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger logger;
    private readonly BootstrapLoggingSettings settings;

    private bool disposed;

    /// <summary>Initializes a bootstrap logger over an already composed logging pipeline.</summary>
    /// <param name="loggerFactory">The logging pipeline to write to. This instance takes ownership of it and disposes it.</param>
    /// <param name="settings">The service identity reported with every record.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loggerFactory" /> or <paramref name="settings" /> is <see langword="null" />.</exception>
    public BootstrapLogger(ILoggerFactory loggerFactory, BootstrapLoggingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(settings);

        this.loggerFactory = loggerFactory;
        this.settings = settings;
        this.logger = loggerFactory.CreateLogger(LogCategory);
    }

    /// <summary>Composes the bootstrap logging pipeline from the process environment.</summary>
    /// <returns>A bootstrap logger owning the composed pipeline, which the caller disposes.</returns>
    public static BootstrapLogger CreateFromEnvironment()
    {
        var settings = BootstrapLoggingSettings.FromEnvironment();

        return new BootstrapLogger(CreateLoggerFactory(settings), settings);
    }

    /// <summary>Reports that the host process has begun composing itself, and which build is composing.</summary>
    /// <remarks>
    /// The version and the revision are both reported because they answer different questions. The version states what
    /// the process promises across the four surfaces
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md">ADR 0004</see> versions; the revision
    /// names the commit it was built from, which is what makes a report from a deployment the reader did not build
    /// reproducible. A build with neither a repository beside it nor a revision supplied to it reports <c>unknown</c>.
    /// </remarks>
    public void RecordHostStarting() =>
        this.LogHostStarting(
            this.settings.ServiceName,
            this.settings.EnvironmentName,
            this.settings.ServiceVersion,
            this.settings.ServiceRevision);

    /// <summary>Reports how many deployment-provisioned configuration files were layered into the host's configuration.</summary>
    /// <param name="fileCount">The number of files layered in, which is zero when the deployment provisioned none.</param>
    /// <remarks>
    /// The count is what makes a mount that did not arrive visible at the moment it matters. A directory the deployment
    /// named is required to exist, so an absent one already fails startup; a mounted directory that is empty is a
    /// legitimate intermediate state during a rollout and reports itself here as zero rather than as a failure.
    /// </remarks>
    public void RecordProvisionedConfigurationFiles(int fileCount) =>
        this.LogProvisionedConfigurationFiles(this.settings.ServiceName, fileCount);

    /// <summary>Reports that the host process is ending because of an exception that escaped composition or the run.</summary>
    /// <param name="exception">The exception that ended the process.</param>
    public void RecordHostFailed(Exception exception) => this.LogHostFailed(exception, this.settings.ServiceName);

    /// <summary>Reports that the host process has shut down without an unhandled failure.</summary>
    public void RecordHostStopped() => this.LogHostStopped(this.settings.ServiceName);

    /// <summary>Releases the owned logging pipeline. Repeated calls do nothing.</summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.loggerFactory.Dispose();
    }

    private static ILoggerFactory CreateLoggerFactory(BootstrapLoggingSettings settings) =>
        LoggerFactory.Create(logging =>
        {
            // The Logging configuration section is deliberately not bound here. Configuration that fails to load or
            // bind is one of the failures this pipeline exists to report, so it must not be a prerequisite for it.
            logging.SetMinimumLevel(LogLevel.Information);

            // The console keeps a native process under systemd and a container under a log driver diagnosable even
            // where no collector is configured, which is precisely the deployment most likely to lose a crash record.
            logging.AddConsole();

            logging.AddOpenTelemetry(openTelemetry =>
            {
                openTelemetry.IncludeFormattedMessage = true;
                openTelemetry.IncludeScopes = true;

                openTelemetry.SetResourceBuilder(CreateResourceBuilder());

                if (settings.ExportsToCollector)
                {
                    openTelemetry.AddOtlpExporter((_, processorOptions) =>
                        processorOptions.ExportProcessorType = ExportProcessorType.Simple);
                }
            });
        });

    /// <summary>Composes the resource the startup records are exported with.</summary>
    /// <returns>The resource builder the bootstrap logging pipeline exports under.</returns>
    /// <remarks>
    /// The service is left unnamed, with no <c>AddService</c> call, because that is what the container pipeline does
    /// too. Naming it here would agree with that pipeline only while <c>OTEL_SERVICE_NAME</c> is set and would
    /// otherwise report this process under a second identity, since the SDK's own fallback is
    /// <c>unknown_service:{processName}</c>. The build is what both pipelines do put on the resource, as the version and
    /// the source revision and from the same stamped source, so a startup record and everything exported after it name
    /// one build.
    /// </remarks>
    internal static ResourceBuilder CreateResourceBuilder() =>
        ResourceBuilder.CreateDefault().AddStampedBuildIdentity();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Host {ServiceName} is starting in environment {EnvironmentName} at version {ServiceVersion} built from revision {ServiceRevision}.")]
    private partial void LogHostStarting(
        string serviceName,
        string environmentName,
        string serviceVersion,
        string serviceRevision);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Host {ServiceName} layered {FileCount} deployment-provisioned configuration files below the environment.")]
    private partial void LogProvisionedConfigurationFiles(string serviceName, int fileCount);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Host {ServiceName} ended with an unhandled exception during composition, startup, or run.")]
    private partial void LogHostFailed(Exception exception, string serviceName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Host {ServiceName} stopped.")]
    private partial void LogHostStopped(string serviceName);
}
