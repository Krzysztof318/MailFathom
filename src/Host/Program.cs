// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Host;
using MailFathom.Host.Configuration;
using MailFathom.Host.Hosting;
using MailFathom.Host.Observability;
using MailFathom.Host.Security;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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

    builder.AddServiceDefaults();
    builder.Services.AddProblemDetails();
    builder.Services.AddSingleton(TimeProvider.System);
    // ReferenceOnly is the default, so a deployment that configures nothing gets the mode under which a plain-text value
    // where a reference belongs fails startup instead of authenticating.
    builder.Services.AddSecretResolution(
        builder.Configuration.GetValue("Secrets:Interpretation", SecretValueInterpretation.ReferenceOnly));
    // The non-HTTP dependency classes only. HttpClient traffic, which is how the AI provider clients reach a hosted
    // model, is already wrapped once by AddStandardResilienceHandler in the service defaults above.
    builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
    // Bound strictly: mail transport is security-sensitive, and a misspelled key such as a singular
    // "PermittedAuthenticationMechanism" would otherwise be ignored and silently replaced by the default allow-list.
    builder.Services.AddOptions<MailSynchronizationOptions>()
        .Bind(
            builder.Configuration.GetSection("MailSynchronization"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // The one mail synchronization rule that needs the current date, which no attribute on a bound options graph can
    // reach, arrives through the options framework's own validator seam rather than as a second validation mechanism.
    builder.Services.AddSingleton<IValidateOptions<MailSynchronizationOptions>, MailSynchronizationWindowValidator>();
    // The host stops awaiting StopAsync once its own shutdown budget expires, so a drain configured beyond that budget
    // would be accepted and never honored: the process would exit with the work still running. The budget is therefore
    // derived from the configured drain instead of being left on the framework default. Read from configuration
    // directly for the same reason the text search configuration below is — the value has to be known while the host
    // is being built, before a container that could resolve an options snapshot exists. It is restart-required, which
    // is what a shutdown budget is by nature.
    builder.Services.Configure<HostOptions>(hostOptions => hostOptions.ShutdownTimeout =
        MailSynchronizationOptions.ResolveHostShutdownBudget(builder.Configuration.GetValue(
            "MailSynchronization:ShutdownDrainTimeout",
            new MailSynchronizationOptions().ShutdownDrainTimeout)));
    // Bound strictly for the same reason as mail transport: a misspelled "Passwrod" would leave the secret block
    // undiscovered, start the host on a passwordless connection string, and surface as an authentication failure later.
    builder.Services.AddOptions<PersistenceOptions>()
        .Bind(
            builder.Configuration.GetSection("Persistence"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // Bound strictly like the blocks above: a misspelled "SnippetsPerEmails" would leave the configured bound
    // undiscovered and search would quietly keep showing the default amount of every matched message.
    builder.Services.AddOptions<MailboxSearchOptions>()
        .Bind(
            builder.Configuration.GetSection("MailboxSearch"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<MailExtractionBackfillOptions>()
        .Bind(
            builder.Configuration.GetSection("MailExtractionBackfill"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<EmailContentOptions>()
        .Bind(
            builder.Configuration.GetSection("EmailContent"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // The published snapshot, not the bound one, is what every consumer reads: a reload whose secret references do not
    // resolve is rejected and leaves the previous configuration active for new operations.
    builder.Services.AddSingleton<DatabaseConnectionSettingsMapper>();
    builder.Services.AddSingleton<SecretConfigurationValidator>();
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<MailSynchronizationOptions>(
        provider.GetRequiredService<IOptionsMonitor<MailSynchronizationOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<SecretConfigurationValidator>()
            .FindMailConfigurationErrorsAsync(candidate, cancellationToken),
        "MailSynchronization",
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<MailSynchronizationOptions>>>()));
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<PersistenceOptions>(
        provider.GetRequiredService<IOptionsMonitor<PersistenceOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<SecretConfigurationValidator>()
            .FindPersistenceConfigurationErrorsAsync(candidate, cancellationToken),
        "Persistence",
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<PersistenceOptions>>>()));
    builder.Services.AddSingleton<ISettingsSnapshot<MailSynchronizationOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailSynchronizationOptions>>());
    builder.Services.AddSingleton<ISettingsSnapshot<PersistenceOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<PersistenceOptions>>());
    // One work unit runs against one snapshot: the enclosing run hands its own down, and a scope with no enclosing run
    // falls back to the published one. That is what keeps the transport security policy a work unit validates against,
    // the material it connects with, and the account list it was scheduled from all from the same reload.
    builder.Services.AddScoped<ScopedMailSynchronizationSettings>();
    builder.Services.AddScoped(provider => provider.GetRequiredService<ScopedMailSynchronizationSettings>().Current);
    builder.Services.AddScoped<IMailTransportSecurityPolicyReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailSynchronizationWindowReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IRemotelyDeletedEmailDispositionReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IMailAccountCatalog>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
    builder.Services.AddScoped<IImapAccountSettingsProvider, ConfiguredImapAccountSettingsProvider>();
    builder.Services.AddScoped(provider =>
    {
        var synchronizationSettings = provider.GetRequiredService<MailSynchronizationOptions>();
        return new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = synchronizationSettings.MaxMetadataBatchSize,
            MaxRawMimeBytes = synchronizationSettings.MaxRawMimeBytes,
            MaxMetadataBatchesPerRun = synchronizationSettings.MaxMetadataBatchesPerRun,
            MaxReconciledEmailsPerRun = synchronizationSettings.MaxReconciledEmailsPerRun,
        };
    });
    builder.Services.AddScoped(provider =>
    {
        var synchronizationSettings = provider.GetRequiredService<MailSynchronizationOptions>();
        return new EmailMimeExtractionOptions
        {
            MaxPartCount = synchronizationSettings.MaxMimePartCount,
            MaxNestingDepth = synchronizationSettings.MaxMimeNestingDepth,
            MaxExtractedTextCharacters = synchronizationSettings.MaxExtractedTextCharacters,
        };
    });
    builder.Services.AddScoped(provider => new EmailContentReadOptions
    {
        MaxBodyCharacters = provider.GetRequiredService<IOptions<EmailContentOptions>>().Value.MaxBodyCharacters,
    });
    builder.Services.AddScoped(provider =>
    {
        var backfillSettings = provider.GetRequiredService<IOptions<MailExtractionBackfillOptions>>().Value;
        return new StoredEmailExtractionBackfillOptions
        {
            BatchSize = backfillSettings.BatchSize,
            MaxBatchesPerRun = backfillSettings.MaxBatchesPerRun,
        };
    });
    builder.Services.AddSingleton(provider => new PersistenceConcurrencyOptions
    {
        MaximumCommitAttempts = provider.GetRequiredService<IOptions<PersistenceOptions>>().Value.MaximumConcurrencyCommitAttempts,
    });
    // A singleton rather than a scoped value: the bound is a deployment-wide privacy control, so every search in the
    // process applies the one an operator configured rather than whichever snapshot a scope happened to open under.
    builder.Services.AddSingleton(provider =>
    {
        var searchSettings = provider.GetRequiredService<IOptions<MailboxSearchOptions>>().Value;
        return EmailSearchSnippetBounds.Create(searchSettings.SnippetsPerEmail, searchSettings.WordsPerSnippet);
    });
    // What the startup probe reports. Both gates reach a remote dependency, so both take as long as that dependency
    // does, and an orchestrator's startup probe is what turns that interval into an extended grace period rather than
    // into a failing instance. The probe answers from this tracker rather than from the order the framework happens to
    // start its hosted services in.
    builder.Services.AddSingleton(new HostStartupGates(
        HostStartupGate.SecretConfiguration,
        HostStartupGate.DatabaseSchema));
    builder.Services.AddHealthChecks()
        .AddCheck<HostStartupGatesHealthCheck>(HostStartupGatesHealthCheck.Name, tags: [HealthProbe.Startup.Tag]);
    // The validator is registered ahead of the worker so hosted-service ordering reinforces the StartingAsync ordering
    // rather than depending on it alone, and ahead of the infrastructure so an operator who mistyped several references
    // reads one aggregated report rather than whichever failure the database happened to hit first.
    builder.Services.AddHostedService<SecretConfigurationStartupValidator>();
    // Registered after the startup gate so the first snapshot is proven before either begins accepting reloaded ones.
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailSynchronizationOptions>>());
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<PersistenceOptions>>());
    // The secret blocks come from the published snapshot on every read, so a reference an operator repoints reaches the
    // next physical connection instead of waiting for a restart.
    // The text search configuration is taken once, from configuration directly, because the EF Core model has to be
    // described before the container that would resolve an options snapshot exists — and because the value is compiled
    // into the schema, so adopting a reloaded one would leave the index describing the configuration it replaced. An
    // unsupported name throws here and is recorded by the bootstrap logger; PersistenceOptions validates the same value
    // on start, which is what reports the supported alternatives to an operator.
    var configuredTextSearchConfiguration = builder.Configuration["Persistence:TextSearchConfiguration"];
    builder.Services.AddInfrastructure(
        provider => provider.GetRequiredService<DatabaseConnectionSettingsMapper>()
            .Map(provider.GetRequiredService<ISettingsSnapshot<PersistenceOptions>>().Current),
        string.IsNullOrWhiteSpace(configuredTextSearchConfiguration)
            ? PostgresTextSearchConfiguration.Default
            : PostgresTextSearchConfiguration.Create(configuredTextSearchConfiguration));
    // After the context is registered, because enrichment layers onto an existing registration rather than creating
    // one, and read from configuration directly for the same reason the text search configuration is: the value has to
    // be known before the container that would resolve an options snapshot exists. PersistenceOptions validates the
    // same key on start, which is what reports an out-of-range value to an operator.
    // Readiness alone. The database is a dependency a request needs, so an unreachable one must remove this instance
    // from traffic; it must never reach the liveness probe, because restarting a process cannot fix a database and
    // would turn one outage into an outage plus a restart loop.
    builder.AddDatabaseHealthAndTelemetry(
        TimeSpan.FromSeconds(builder.Configuration.GetValue(
            "Persistence:CommandTimeoutSeconds",
            HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)),
        probeTags: [HealthProbe.Readiness.Tag]);
    // Ahead of the workers so no unit of work reads or writes mail before the schema this build expects is proven, and
    // after the infrastructure that registers the inspector it resolves.
    builder.Services.AddHostedService<DatabaseSchemaStartupGate>();
    builder.Services.AddHostedService<MailSynchronizationCoordinator>();
    builder.Services.AddHostedService<MailExtractionBackfillWorker>();
    // Registered whether or not the endpoint is enabled, because it is the warning that decides whether it has anything
    // to say. Registering it conditionally would put the same condition in two places.
    builder.Services.AddHostedService<McpTransportAuthenticationWarning>();
    builder.Services.AddHostedService<McpTransportEncryptionWarning>();
    builder.Services.AddHostedService<McpRateLimitingStartupReport>();

    // Read once and registered, so the value that decides the route is the one every consumer resolves. Whether the
    // endpoint exists is decided while the application is being built, before a container that could resolve a snapshot
    // exists, and a second read of a reloadable source could otherwise map the endpoint from one value while the missing
    // authentication was warned about from another.
    //
    // Bound strictly like the other security-sensitive sections: a misspelled "Enabeld" would leave the endpoint off
    // while an operator believed they had turned it on.
    var mcpEndpointSettings = McpEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(mcpEndpointSettings));

    // Validated here rather than through ValidateOnStart, because the section is read before a container exists and the
    // decisions it carries — whether to map the endpoint, which scheme protects it — are taken during composition. The
    // secrets it names are proven separately, by the startup validator that proves every other section's.
    var mcpEndpointConfigurationErrors = new List<string>(mcpEndpointSettings.FindConfigurationErrors());

    // Read from the root configuration rather than from the bound section, because a listener Kestrel was configured
    // with elsewhere survives the ones bound below and would serve the same route without the TLS a profile adds.
    mcpEndpointConfigurationErrors.AddRange(ConfiguredKestrelEndpoints.FindHttpsProfileConflicts(
        builder.Configuration,
        mcpEndpointSettings.Https));

    if (mcpEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            mcpEndpointConfigurationErrors);
    }

    // Mapped once, next to the decision that reads it, so the numbers the limiters are built from and the numbers the
    // startup report states are the same reading of the same settings. Null means an operator turned limiting off, which
    // is the one case in which no limiter is registered at all rather than one configured to permit everything.
    var mcpRateLimits = mcpEndpointSettings is { Enabled: true, RateLimiting.Enabled: true }
        ? mcpEndpointSettings.RateLimiting.ToRateLimits()
        : null;

    if (mcpEndpointSettings.Enabled)
    {
        // The tools read the local mailbox copy through the use cases the infrastructure registration above already
        // added, so the protocol surface adds no port of its own.
        builder.Services.AddMailFathomServer();
        builder.Services.AddMcpTransportSecurity(mcpEndpointSettings);

        // A certificate is asked for while the connection is being established or it never arrives at all, so this is a
        // decision the server has to take before it is listening rather than one a request can reach. Which call states
        // it depends on how the listener was configured, and that is not a second mechanism for one concern: one
        // decision — whether any trust profile exists to judge a certificate — reaches whichever listener shape this
        // deployment has. ConfigureHttpsDefaults below applies to a listener built from a URL, and a listener that
        // supplies its own SslServerAuthenticationOptions never consults it, which is why the HTTPS profiles restate
        // the same posture in their own handshake callback.
        if (mcpEndpointSettings.ClientCertificateProfiles.Count > 0 && !mcpEndpointSettings.Https.TerminatesTls)
        {
            builder.WebHost.RequestMcpClientCertificates();
        }
    }

    // Registered whether or not any profile is configured, because the store is what the certificates are loaded into
    // and disposed from, and an unconfigured deployment simply loads none.
    builder.Services.AddSingleton<McpServerCertificateStore>();

    if (mcpEndpointSettings.Enabled && mcpEndpointSettings.Https.TerminatesTls)
    {
        // Binding a listener here replaces the URLs the host was otherwise configured with, which is what keeps a
        // clear-text listener from staying open behind an endpoint an operator configured HTTPS for. The callback runs
        // when the server is constructed, after the container exists, so the store it reads is the one the composition
        // root has already loaded.
        builder.WebHost.ConfigureKestrel(kestrelOptions => McpHttpsEndpointBinder.Bind(
            kestrelOptions,
            mcpEndpointSettings.Https,
            kestrelOptions.ApplicationServices.GetRequiredService<McpServerCertificateStore>(),
            mcpEndpointSettings.ClientCertificateProfiles.Count > 0));
    }

    if (mcpRateLimits is not null)
    {
        builder.Services.AddMcpRateLimiting(mcpRateLimits);
    }

    // Read once, like the MCP section and for the same reason: it decides which sockets are opened and which routes
    // exist, both of which are settled while the application is being built. Bound strictly, so a misspelled key cannot
    // leave a deployment serving a posture nobody selected.
    var healthEndpointSettings = HealthEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(healthEndpointSettings));

    // The addresses the application listener would bind, read before anything is added to the Kestrel configuration
    // below. Where the MCP HTTPS profiles bind their own listeners, they are the application listener, and the
    // URL-shaped addresses are already being ignored.
    var applicationListenerUrls = ConfiguredApplicationListeners.ResolveUrls(builder.Configuration);
    var mcpTerminatesTls = mcpEndpointSettings.Enabled && mcpEndpointSettings.Https.TerminatesTls;
    IReadOnlyCollection<int> applicationListenerPorts = mcpTerminatesTls
        ? [.. mcpEndpointSettings.Https.Endpoints.Select(static endpoint => endpoint.Port)]
        : ConfiguredApplicationListeners.ListenerPorts(applicationListenerUrls);

    var healthEndpointConfigurationErrors = healthEndpointSettings.FindConfigurationErrors(applicationListenerPorts);

    if (healthEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            HealthEndpointOptions.SectionName,
            typeof(HealthEndpointOptions),
            healthEndpointConfigurationErrors);
    }

    // Registered whether or not the transport terminates TLS, because the holder is what a certificate is loaded into
    // and disposed from, and a clear-text deployment simply loads none.
    builder.Services.AddSingleton<HealthEndpointCertificate>();

    if (healthEndpointSettings.Enabled)
    {
        // Kestrel ignores the URL-shaped addresses as soon as any listener is bound in code, so opening the probe
        // listener would silently take the application listener away from a deployment that states its port through
        // ASPNETCORE_HTTP_PORTS or ASPNETCORE_URLS. Restating those addresses as Kestrel endpoints hands the same
        // strings back to the framework's own parser, which binds the same sockets it would have bound. Nothing is
        // restated where the addresses were already being ignored: a deployment that names its own Kestrel endpoints
        // keeps them, and one whose MCP HTTPS profiles bind in code keeps the promise that no clear-text listener
        // stays open behind them.
        if (!mcpTerminatesTls && !ConfiguredKestrelEndpoints.AnyConfigured(builder.Configuration))
        {
            builder.Configuration.AddInMemoryCollection(
                ConfiguredApplicationListeners.AsKestrelEndpointConfiguration(applicationListenerUrls));
        }

        // The callback runs when the server is constructed, after the container exists and after the composition root
        // below has loaded the certificate the TLS listener presents.
        builder.WebHost.ConfigureKestrel(kestrelOptions => HealthEndpointListenerBinder.Bind(
            kestrelOptions,
            healthEndpointSettings,
            kestrelOptions.ApplicationServices.GetRequiredService<HealthEndpointCertificate>()));
    }

    var app = builder.Build();

    // Before the server starts rather than from a hosted service, because a hosted service could be started after the
    // web host and a certificate proven then would be proven after the listener was already open. A profile whose
    // material is missing, expired, or issued for another domain therefore fails startup with nothing listening.
    if (mcpEndpointSettings.Enabled && mcpEndpointSettings.Https.TerminatesTls)
    {
        await app.Services.GetRequiredService<McpServerCertificateStore>()
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    // For the same reason, and with the same outcome: a TLS transport whose material is unusable fails startup with
    // nothing listening rather than downgrading the probe port to clear text.
    await app.Services.GetRequiredService<HealthEndpointCertificate>()
        .LoadAsync(app.Lifetime.ApplicationStopping);

    app.UseExceptionHandler();

    if (healthEndpointSettings.Enabled)
    {
        // A probe reports healthy over no checks at all, because the aggregate of nothing is healthy. Asserting the
        // composed result rather than the wiring is what catches a tag that stopped matching: readiness answering
        // without consulting the database would keep an instance in traffic that cannot serve a request.
        var unansweredProbes = HealthProbeEndpoints.FindUnansweredProbes(
            app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        if (unansweredProbes.Count > 0)
        {
            throw new OptionsValidationException(
                HealthEndpointOptions.SectionName,
                typeof(HealthEndpointOptions),
                unansweredProbes);
        }

        // Ahead of everything the MCP endpoint adds, so a request for the protocol surface that arrived on the probe
        // port is refused before it reaches CORS, authentication, or the rate limiter, and a probe request that arrived
        // on the application port is refused before it can report dependency state to whoever can reach it.
        app.UseHealthEndpointIsolation(healthEndpointSettings.ListenerPorts);
        app.MapHealthProbes();
    }

    app.MapGet("/", () => Results.Ok(new { service = "MailFathom", status = "ready" }));

    if (mcpEndpointSettings.Enabled)
    {
        // CORS first, so a browser's preflight is answered by the middleware that owns preflight rather than reaching a
        // check written for real requests. The origin check then runs ahead of authentication, because whether this
        // deployment serves a page's origin does not depend on which credential the page attached.
        app.UseCors();

        if (mcpEndpointSettings.AllowsOAuth)
        {
            // The policy reaches the MCP route as endpoint metadata, and the protected resource metadata document has
            // none to carry it: the authentication handler publishes it instead of a mapped route. A browser client
            // reads that document before it holds any credential, so without the policy applied to its path the one
            // response that says where to authorize is the one a page cannot read.
            var protectedResourceMetadataPath = mcpEndpointSettings.OAuth.ProtectedResourceMetadataPath();

            app.UseWhen(
                context => context.Request.Path.Equals(protectedResourceMetadataPath, StringComparison.OrdinalIgnoreCase),
                metadataDocument => metadataDocument.UseCors(McpTransportSecurityExtensions.CorsPolicyName));
        }

        app.UseMcpOriginValidation();

        if (mcpEndpointSettings.ClientCertificateProfiles.Count > 0)
        {
            // Ahead of authentication, because which client application is calling and which credential it presents are
            // separate questions: a request from a program this deployment does not serve is turned away before any
            // credential is read.
            app.UseMcpClientCertificateValidation(mcpEndpointSettings.ToClientCertificateTrustProfiles());
        }

        var mcpEndpoint = app
            .MapMcp(McpEndpointRoute.Path)
            .RequireCors(McpTransportSecurityExtensions.CorsPolicyName);

        if (mcpEndpointSettings.RequiresAuthentication)
        {
            // Authentication also serves the protected resource metadata document, which the MCP authentication scheme
            // publishes as a request handler rather than as a route, so the middleware runs whether or not the request
            // that follows carries a credential.
            app.UseAuthentication();
        }

        if (mcpRateLimits is not null)
        {
            // Behind authentication, so a per-client limit is counted under the identity that established rather than
            // under something the caller chose, and ahead of authorization, so a request that is about to be refused for
            // its credential still spends anonymous capacity — otherwise a flood of bad credentials would be the one
            // kind of traffic the endpoint served without limit.
            app.UseRateLimiter();

            // On the endpoint for the same reason authorization is: the readiness and liveness endpoints have to keep
            // answering while this one is refusing. The process-wide half of the policy cannot be attached here, because
            // an endpoint resolves one limiter, so it rides on the global limiter and excludes every other route itself.
            mcpEndpoint.RequireRateLimiting(McpRateLimiting.PolicyName);
        }

        if (mcpEndpointSettings.RequiresAuthentication)
        {
            app.UseAuthorization();

            // On the endpoint rather than as a fallback policy, so the readiness response and the health endpoints keep
            // answering unauthenticated while everything the MCP route exposes is covered by the one requirement it
            // carries. Under the stateless transport that route is the post alone; a get or a delete is not mapped at
            // all, so there is no second entry into the protocol surface for a requirement to miss.
            mcpEndpoint.RequireAuthorization(McpAccessPolicy.PolicyName);
        }
    }

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
