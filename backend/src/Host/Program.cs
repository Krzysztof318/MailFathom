// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.Observability;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;

// Composed before anything else, CreateBuilder included, so that a malformed appsettings.json, a failure during
// composition, and a failed host start are all reported rather than only printed. The pipeline the container owns
// does not exist until Build has returned, and on a startup failure it never flushes.
using var bootstrapLogger = BootstrapLogger.CreateFromEnvironment();
bootstrapLogger.RecordHostStarting();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Before anything reads configuration, so a mounted ConfigMap is ordinary configuration to every binding below
    // rather than a second source consulted afterwards. The files land beneath the environment-variable provider, which
    // keeps an environment variable an override of a mounted file rather than something a stale mount can beat.
    var provisionedConfigurationFileCount = builder.Configuration.AddProvisionedConfiguration();
    bootstrapLogger.RecordProvisionedConfigurationFiles(provisionedConfigurationFileCount);

    // Above the files and below every override an operator reaches for, so a persisted setting binds exactly as one
    // from a file while a bad persisted value stays repairable without first reaching the database that holds it. It
    // is read here rather than by the provider it feeds because resolving the credential that opens that database is
    // asynchronous, and a configuration provider's own load is not.
    //
    // No token, because the host whose lifetime would supply one does not exist until Build has returned. Every leg of
    // the read carries a bound of its own instead — the credential retrieval, the connection, and the command — so a
    // credential source or a database that never answers fails startup rather than hanging it.
    var rootSettings = await builder.Configuration.AddRootSettingsAsync(CancellationToken.None);
    bootstrapLogger.RecordRootSettingsVersion(rootSettings.Version);

    // Once every source exists and before anything binds one, because this asks whether a value can reach its reader at
    // all rather than what the value says. The few settings only the environment can deliver were read before this line
    // — by the pipeline above, by the OpenTelemetry exporter, by the .NET host, by OpenSSL — so a value that arrived
    // from anywhere else is refused here instead of being accepted and ignored.
    EnvironmentOnlySettings.RejectMisplacedValues(builder.Configuration, Environment.GetEnvironmentVariable);

    // Every service this process runs on, registered in one callable place rather than here, because top-level
    // statements cannot be called: a composition root written in them is one no test can build, and an unregistered
    // dependency then reaches an operator as an exception out of a worker instead of as a suite that failed.
    var composition = HostComposition.Compose(builder);

    var app = builder.Build();

    // Before the server starts rather than from a hosted service, because a hosted service could be started after the
    // web host and a certificate proven then would be proven after the listener was already open. A profile whose
    // material is missing, expired, or issued for another domain therefore fails startup with nothing listening.
    if (composition.Mcp.Enabled && composition.Mcp.TerminatesTls)
    {
        await app.Services.GetRequiredService<TransportServerCertificateStore>()
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    if (composition.Admin.Enabled && composition.Admin.TerminatesTls)
    {
        await app.Services.GetRequiredKeyedService<TransportServerCertificateStore>(HostComposition.AdminCertificateStoreKey)
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    // For the same reason, and with the same outcome: a TLS transport whose material is unusable fails startup with
    // nothing listening rather than downgrading the probe port to clear text.
    await app.Services.GetRequiredService<HealthEndpointCertificate>()
        .LoadAsync(app.Lifetime.ApplicationStopping);

    // Every middleware and every route this deployment serves, composed in one callable place rather than here, for
    // the reason the service graph is: top-level statements cannot be called, and a misordered pipeline written in
    // them is one no test can reach — it fails nothing at startup and shows up as a deployment answering the wrong
    // way.
    HostPipeline.Compose(app, composition);

    await app.RunAsync();

    bootstrapLogger.RecordHostStopped();
}
catch (Exception exception)
{
    // The exception leaves unchanged, so the runtime still writes it to standard error and the exit code is the one
    // an unhandled failure produces. The catch exists only to add the record that survives the process ending.
    bootstrapLogger.RecordHostFailed(exception);

    throw;
}
