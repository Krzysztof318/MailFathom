// Copyright © 2026 Krzysztof Kasprowicz

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace MailMcp.Host.Observability;

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
/// which makes delivery independent of process teardown; three records per process make the cost irrelevant.
/// </para>
/// <para>
/// The instance takes ownership of the <see cref="ILoggerFactory" /> it is constructed with and disposes it. It is
/// deliberately absent from the container: it has to be usable before the container exists, and a container that
/// disposed it would take the pipeline away exactly when a shutdown failure needs reporting.
/// </para>
/// </remarks>
internal sealed partial class BootstrapLogger : IDisposable
{
    private const string LogCategory = "MailMcp.Host.Startup";

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

    /// <summary>Composes the bootstrap logging pipeline for the host that is starting.</summary>
    /// <param name="configuration">The configuration of the host being composed.</param>
    /// <param name="environment">The environment of the host being composed.</param>
    /// <returns>A bootstrap logger owning the composed pipeline, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> or <paramref name="environment" /> is <see langword="null" />.</exception>
    public static BootstrapLogger Create(IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = BootstrapLoggingSettings.From(configuration, environment);

        return new BootstrapLogger(CreateLoggerFactory(settings), settings);
    }

    /// <summary>Reports that the host process has begun composing itself.</summary>
    public void RecordHostStarting() =>
        this.LogHostStarting(this.settings.ServiceName, this.settings.EnvironmentName, this.settings.ServiceVersion);

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
                openTelemetry.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(settings.ServiceName, serviceVersion: settings.ServiceVersion));

                if (settings.ExportsToCollector)
                {
                    openTelemetry.AddOtlpExporter((_, processorOptions) =>
                        processorOptions.ExportProcessorType = ExportProcessorType.Simple);
                }
            });
        });

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Host {ServiceName} is starting in environment {EnvironmentName} at version {ServiceVersion}.")]
    private partial void LogHostStarting(string serviceName, string environmentName, string serviceVersion);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Host {ServiceName} ended with an unhandled exception during composition, startup, or run.")]
    private partial void LogHostFailed(Exception exception, string serviceName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Host {ServiceName} stopped.")]
    private partial void LogHostStopped(string serviceName);
}
