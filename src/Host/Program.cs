// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Host;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Hosting;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Observability;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Authentication;
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
    // A configuration root of its own rather than a section of Persistence: the database is the first thing sealed
    // under the ring and there is no reason it is the last, and a root is also what gives the key material its own
    // secret-name uniqueness scope. ADR 0005 records the whole decision.
    builder.Services.AddOptions<DataEncryptionOptions>()
        .Bind(
            builder.Configuration.GetSection("DataEncryption"),
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
    builder.Services.AddSingleton(provider => new ValidatedSettingsSnapshot<DataEncryptionOptions>(
        provider.GetRequiredService<IOptionsMonitor<DataEncryptionOptions>>(),
        (candidate, cancellationToken) => provider.GetRequiredService<SecretConfigurationValidator>()
            .FindDataEncryptionConfigurationErrorsAsync(candidate, cancellationToken),
        "DataEncryption",
        provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<DataEncryptionOptions>>>()));
    builder.Services.AddSingleton<ISettingsSnapshot<MailSynchronizationOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailSynchronizationOptions>>());
    builder.Services.AddSingleton<ISettingsSnapshot<PersistenceOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<PersistenceOptions>>());
    builder.Services.AddSingleton<ISettingsSnapshot<DataEncryptionOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<DataEncryptionOptions>>());
    // The key ring reads the published snapshot on every operation, so a key an operator adds reaches the next seal or
    // open without a restart — which is the half of a rotation that must not need one.
    builder.Services.AddDataEncryption(provider => DataEncryptionKeyRingMapper.Map(
        provider.GetRequiredService<ISettingsSnapshot<DataEncryptionOptions>>().Current));
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
    builder.Services.AddScoped<IMailOAuthSettingsProvider, ConfiguredMailOAuthSettingsProvider>();
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
    builder.Services.AddScoped(provider =>
    {
        var contentSettings = provider.GetRequiredService<IOptions<EmailContentOptions>>().Value;
        return new EmailContentReadOptions
        {
            MaxBodyCharacters = contentSettings.MaxBodyCharacters,
            MaxCharactersPerRead = contentSettings.MaxCharactersPerRead,
        };
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
    builder.Services.AddHostedService(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<DataEncryptionOptions>>());
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
    builder.Services.AddHostedService<ReverseProxyTrustWarning>();
    builder.Services.AddHostedService<TransportRateLimitingStartupReport>();
    // Composed from the environment rather than resolved from the container, because the value it reports is one
    // OpenSSL read while it initialized and no configuration source can influence it afterwards. Registered
    // unconditionally for the same reason the warnings above are: the condition belongs in one place.
    builder.Services.AddHostedService(provider => OpenSslConfigurationWarning.FromEnvironment(
        provider.GetRequiredService<ILogger<OpenSslConfigurationWarning>>()));

    // Read before the surfaces, because it is the one posture they all sit behind: which peers this process accepts a
    // public scheme and host from. Read once for the same reason every section below is — the pipeline's
    // forwarded-header policy is composed from it, and the encryption warning states the posture this settles.
    var reverseProxySettings = ReverseProxyOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(reverseProxySettings));

    var reverseProxyConfigurationErrors = reverseProxySettings.FindConfigurationErrors();

    if (reverseProxyConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            ReverseProxyOptions.SectionName,
            typeof(ReverseProxyOptions),
            reverseProxyConfigurationErrors);
    }

    builder.Services.AddTrustedReverseProxy(reverseProxySettings);

    // Read once and registered, so the value that decides the route is the one every consumer resolves. Whether the
    // endpoint exists is decided while the application is being built, before a container that could resolve a snapshot
    // exists, and a second read of a reloadable source could otherwise map the endpoint from one value while the missing
    // authentication was warned about from another.
    //
    // Bound strictly like the other security-sensitive sections: a misspelled "Enabeld" would leave the endpoint off
    // while an operator believed they had turned it on.
    var mcpEndpointSettings = McpEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(mcpEndpointSettings));

    // Read once, like the MCP section and for the same reason. Administering this service and reading a mailbox
    // through it are different authorities, so the section is separate all the way down: its own listener, its own
    // credentials, and its own authorization servers. Bound strictly, so a misspelled key cannot leave a deployment
    // serving an administrative surface nobody meant to enable.
    var adminEndpointSettings = AdminEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(adminEndpointSettings));

    // Read once, like the two sections above and for the same reason: it decides which sockets are opened and which
    // routes exist, both of which are settled while the application is being built. Bound strictly, so a misspelled key
    // cannot leave a deployment serving a posture nobody selected.
    var healthEndpointSettings = HealthEndpointOptions.ReadFrom(builder.Configuration);
    builder.Services.AddSingleton(Options.Create(healthEndpointSettings));

    // Every listener this process opens is bound in code, from the section of the surface it belongs to, so the host's
    // own ways of naming one decide nothing here and are refused rather than ignored. Read at the root, before any
    // section's own errors, because an operator who stated a port that no longer binds anything needs to be told that
    // first — every message below would otherwise describe a section they had not been using.
    var externalListenerConfigurationErrors = ExternalListenerConfiguration.FindConfigurationErrors(builder.Configuration);

    if (externalListenerConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            ExternalListenerConfiguration.KestrelEndpointsSectionName,
            typeof(McpEndpointOptions),
            externalListenerConfigurationErrors);
    }

    // A process serving none of its surfaces opens no listener at all, and Kestrel answers that by binding its own
    // default address — and, where an ASP.NET Core development certificate happens to be installed, a TLS one beside it.
    // That is a socket no section describes, serving whatever a route happens to match, so it is refused here instead.
    if (!mcpEndpointSettings.Enabled && !adminEndpointSettings.Enabled && !healthEndpointSettings.Enabled)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            [
                $"No network surface is enabled: '{McpEndpointOptions.SectionName}:Enabled', "
                + $"'{AdminEndpointOptions.SectionName}:Enabled', and '{HealthEndpointOptions.SectionName}:Enabled' "
                + "are all off, so the process would serve nothing while still holding a socket. Enable the surface this "
                + "deployment exists to serve.",
            ]);
    }

    // Validated here rather than through ValidateOnStart, because the sections are read before a container exists and
    // the decisions they carry — which sockets to open, whether to map an endpoint, which scheme protects it — are taken
    // during composition. The secrets they name are proven separately, by the startup validator that proves every other
    // section's. Each section answers for itself first, so a message about a misspelled key is not delayed behind a
    // question about a socket the deployment may not even share.
    var mcpEndpointConfigurationErrors = mcpEndpointSettings.FindConfigurationErrors();

    if (mcpEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            mcpEndpointConfigurationErrors);
    }

    var adminEndpointConfigurationErrors = adminEndpointSettings.FindConfigurationErrors();

    if (adminEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            AdminEndpointOptions.SectionName,
            typeof(AdminEndpointOptions),
            adminEndpointConfigurationErrors);
    }

    var healthEndpointConfigurationErrors = healthEndpointSettings.FindConfigurationErrors();

    if (healthEndpointConfigurationErrors.Count > 0)
    {
        throw new OptionsValidationException(
            HealthEndpointOptions.SectionName,
            typeof(HealthEndpointOptions),
            healthEndpointConfigurationErrors);
    }

    // Composed once, from what every enabled surface asked for. Surfaces may share a socket — which is what lets a
    // single-node deployment publish one port rather than three, and why both request-serving surfaces default to the
    // same ones — but they may not disagree about it, and this is where that is settled before anything binds.
    var composedListeners = ListenerComposition.Compose(
    [
        .. mcpEndpointSettings.DeclareListeners(),
        .. adminEndpointSettings.DeclareListeners(),
        .. healthEndpointSettings.DeclareListeners(),
    ]);

    if (composedListeners.Errors.Count > 0)
    {
        throw new OptionsValidationException(
            McpEndpointOptions.SectionName,
            typeof(McpEndpointOptions),
            composedListeners.Errors);
    }

    var servedSurfacesByPort = composedListeners.SurfacesByPort();

    // Registered whether or not the endpoint is enabled, because it is the warning that decides whether it has
    // anything to say — the same reason the MCP warnings above are registered unconditionally.
    builder.Services.AddHostedService<AdminTransportSecurityWarning>();

    // Registered here rather than beside the MCP warnings because it reads both surfaces, and unconditionally for the
    // same reason they are: the report is what decides whether either surface has a clear-text port to account for.
    builder.Services.AddHostedService<TransportClearTextRedirectReport>();

    // Mapped once, next to the decision that reads it, so the numbers the limiters are built from and the numbers the
    // startup report states are the same reading of the same settings. Null means an operator turned limiting off, or
    // the endpoint is not served at all, which is the one case in which no limiter is registered for it rather than one
    // configured to permit everything.
    var mcpRateLimits = mcpEndpointSettings is { Enabled: true, RateLimiting.Enabled: true }
        ? mcpEndpointSettings.RateLimiting.ToRateLimits()
        : null;

    var adminRateLimits = adminEndpointSettings is { Enabled: true, RateLimiting.Enabled: true }
        ? adminEndpointSettings.RateLimiting.ToRateLimits()
        : null;

    // Registered once with every bounded surface rather than once per endpoint. The process-wide limiter is a single
    // property of one options object, so a second registration would replace the first endpoint's concurrency limit
    // instead of adding to it — and it would do so silently, leaving whichever endpoint was registered first unbounded
    // in the half nothing else reports.
    var boundedSurfaces = new List<BoundedTransportSurface>();

    if (mcpRateLimits is not null)
    {
        boundedSurfaces.Add(new BoundedTransportSurface(TransportSurface.Mcp, mcpRateLimits));
    }

    if (adminRateLimits is not null)
    {
        boundedSurfaces.Add(new BoundedTransportSurface(TransportSurface.Admin, adminRateLimits));
    }

    if (boundedSurfaces.Count > 0)
    {
        builder.Services.AddTransportRateLimiting(boundedSurfaces);
    }

    const string adminCertificateStoreKey = "mailfathom.admin";

    // Registered whether or not any profile is configured, because the store is what the certificates are loaded into
    // and disposed from, and an unconfigured deployment simply loads none.
    builder.Services.AddSingleton(provider => new TransportServerCertificateStore(
        mcpEndpointSettings.Https,
        $"{McpEndpointOptions.SectionName}:{nameof(McpEndpointOptions.Https)}",
        provider.GetRequiredService<TlsServerCertificateLoader>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILogger<TransportServerCertificateStore>>()));

    // Keyed, because two endpoints each own a store and the container has to tell them apart. Each store loads and
    // disposes only its own endpoint's material, which is what keeps one endpoint's certificates out of the other's
    // configuration — a shared socket is answered by consulting both in turn rather than by merging them.
    builder.Services.AddKeyedSingleton(adminCertificateStoreKey, (provider, _) => new TransportServerCertificateStore(
        adminEndpointSettings.Https,
        $"{AdminEndpointOptions.SectionName}:{nameof(AdminEndpointOptions.Https)}",
        provider.GetRequiredService<TlsServerCertificateLoader>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILogger<TransportServerCertificateStore>>()));

    // Registered whether or not the transport terminates TLS, because the holder is what a certificate is loaded into
    // and disposed from, and a clear-text deployment simply loads none.
    builder.Services.AddSingleton<HealthEndpointCertificate>();

    if (mcpEndpointSettings.Enabled)
    {
        // The tools read the local mailbox copy through the use cases the infrastructure registration above already
        // added, so the protocol surface adds no port of its own.
        builder.Services.AddMailFathomServer();
        builder.Services.AddMcpTransportSecurity(mcpEndpointSettings);
    }

    if (adminEndpointSettings.Enabled)
    {
        builder.Services.AddAdminTransportSecurity(adminEndpointSettings);
    }

    if (composedListeners.Listeners.Count > 0)
    {
        // The callback runs when the server is constructed, after the container exists, so the stores it reads are the
        // ones the composition root has already loaded. A profile-backed socket consults each endpoint's store in turn,
        // which is what lets two surfaces publish different domains on one port without either one's material entering
        // the other's section.
        builder.WebHost.ConfigureKestrel(kestrelOptions => TransportListenerBinder.Bind(
            kestrelOptions,
            composedListeners.Listeners,
            (listener, serverName) =>
                kestrelOptions.ApplicationServices.GetRequiredService<TransportServerCertificateStore>()
                    .Find(listener, serverName)
                ?? kestrelOptions.ApplicationServices
                    .GetRequiredKeyedService<TransportServerCertificateStore>(adminCertificateStoreKey)
                    .Find(listener, serverName),
            kestrelOptions.ApplicationServices.GetRequiredService<HealthEndpointCertificate>));
    }

    // Both endpoints call AddAuthentication, and each call sets the application's one default scheme, so the default
    // is otherwise whichever surface was registered last. It is stated here instead, because the thing that depends on
    // it is not obvious from either registration: UseAuthentication below populates HttpContext.User with the default
    // scheme, and the MCP rate limiter partitions on that user. Left to ordering, enabling the administrative endpoint
    // would silently collapse every authenticated MCP client into the shared anonymous bucket — no failure, just a
    // limit that stopped being per-client.
    if (mcpEndpointSettings is { Enabled: true, RequiresAuthentication: true })
    {
        builder.Services.Configure<AuthenticationOptions>(
            authenticationOptions => authenticationOptions.DefaultScheme = TransportSurface.Mcp.RoutingSchemeName);
    }

    var app = builder.Build();

    // Before the server starts rather than from a hosted service, because a hosted service could be started after the
    // web host and a certificate proven then would be proven after the listener was already open. A profile whose
    // material is missing, expired, or issued for another domain therefore fails startup with nothing listening.
    if (mcpEndpointSettings.Enabled && mcpEndpointSettings.TerminatesTls)
    {
        await app.Services.GetRequiredService<TransportServerCertificateStore>()
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    if (adminEndpointSettings.Enabled && adminEndpointSettings.TerminatesTls)
    {
        await app.Services.GetRequiredKeyedService<TransportServerCertificateStore>(adminCertificateStoreKey)
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    // For the same reason, and with the same outcome: a TLS transport whose material is unusable fails startup with
    // nothing listening rather than downgrading the probe port to clear text.
    await app.Services.GetRequiredService<HealthEndpointCertificate>()
        .LoadAsync(app.Lifetime.ApplicationStopping);

    // One authorization middleware serves every endpoint that requires it, and either endpoint may be the one that
    // adds it. Adding it twice would run every policy twice.
    var authorizationMiddlewareAdded = false;

    // The same holds for the rate limiter, which is one middleware serving whichever endpoints carry a policy. Adding
    // it twice would acquire a lease from both limiters twice and count one request as two.
    var rateLimiterMiddlewareAdded = false;

    // First, ahead of the exception handler and of every isolation check, so that nothing downstream ever reads a
    // scheme or host the proxy already corrected. Discovery, the challenge, and any address composed from a request
    // then agree with the name the client used, and a request from a peer this deployment does not trust passes
    // through with the scheme and host it arrived under.
    app.UseForwardedHeaders();

    app.UseExceptionHandler();

    // Composed from the sockets rather than from the surfaces, because a redirect is a property of the socket a request
    // arrived on. Two surfaces sharing one clear-text port contribute one listener between them, carrying the domains
    // both of them publish — which is what lets each redirect to an HTTPS port of its own from that shared socket, and
    // why one name published by both at different addresses is refused before composition reaches this.
    var clearTextRedirectListeners = composedListeners.Listeners
        .Where(static listener => listener.RedirectsClearText)
        .Select(static listener => new ClearTextRedirectListener(listener.Address.Port, listener.RedirectTargets))
        .ToArray();

    if (clearTextRedirectListeners.Length > 0)
    {
        // Ahead of the isolation middleware and every route, which is what makes a redirecting socket serve nothing but
        // the redirect. Behind it, an administrative path arriving on a redirect port would be answered by isolation
        // with a 404 — a listener refusing a path it does not serve — and the client would read the endpoint as gone
        // rather than as moved.
        app.UseClearTextRedirectToHttps(new ClearTextRedirectTargets(clearTextRedirectListeners));
    }

    // Ahead of everything any surface adds, so a request for a surface this listener does not serve is refused before
    // it reaches CORS, authentication, the client-certificate check, or the rate limiter — and a probe that arrived
    // where the probes are not served is refused before it can report dependency state to whoever can reach it.
    app.UseSurfaceIsolation(servedSurfacesByPort);

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

        app.MapHealthProbes();
    }

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
            var protectedResourceMetadataPath = ProtectedResourceMetadataAddress.PathFor(
                mcpEndpointSettings.OAuth.CanonicalResource());

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
            //
            // Scoped away from the administrative routes rather than added globally. This middleware authenticates with
            // the application's default scheme, pinned above to the MCP surface's, so an administrative request
            // reaching it would have its credential compared against the MCP endpoint's keys before the administrative
            // policy ever ran. Nothing would be disclosed by that — the comparison is constant-time and the result is
            // discarded — but a credential provisioned for one surface must not be offered to the other's handlers.
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments(AdminEndpointOptions.RoutePrefix),
                mcpBranch => mcpBranch.UseAuthentication());
        }

        if (boundedSurfaces.Count > 0)
        {
            // Behind authentication, so the MCP endpoint's per-client limit is counted under the identity that
            // established rather than under something the caller chose, and ahead of authorization, so a request that is
            // about to be refused for its credential still spends anonymous capacity — otherwise a flood of bad
            // credentials would be the one kind of traffic an endpoint served without limit. It is added here whenever
            // either endpoint is bounded, because this is the point that satisfies both orderings: an administrative
            // request reaches it as well, since what the branch above keeps away from those routes is the
            // authentication middleware rather than the pipeline.
            app.UseRateLimiter();
            rateLimiterMiddlewareAdded = true;
        }

        if (mcpRateLimits is not null)
        {
            // On the endpoint for the same reason authorization is: the readiness and liveness endpoints have to keep
            // answering while this one is refusing. The process-wide half of the policy cannot be attached here, because
            // an endpoint resolves one limiter, so it rides on the global limiter and excludes every other route itself.
            mcpEndpoint.RequireRateLimiting(TransportSurface.Mcp.RateLimitingPolicyName);
        }

        if (mcpEndpointSettings.RequiresAuthentication)
        {
            app.UseAuthorization();
            authorizationMiddlewareAdded = true;

            // On the endpoint rather than as a fallback policy, so the readiness response and the health endpoints keep
            // answering unauthenticated while everything the MCP route exposes is covered by the one requirement it
            // carries. Under the stateless transport that route is the post alone; a get or a delete is not mapped at
            // all, so there is no second entry into the protocol surface for a requirement to miss.
            mcpEndpoint.RequireAuthorization(TransportSurface.Mcp.AccessPolicyName);
        }
    }

    if (adminEndpointSettings.Enabled)
    {
        // Ahead of the authorization middleware below, which is what judges this surface's credential, so a request
        // about to be refused for a wrong key has already spent capacity. That ordering is the whole point of bounding
        // this endpoint: unbounded key guessing is what it is exposed to, and the guesses are the traffic authorization
        // turns away.
        if (adminRateLimits is not null && !rateLimiterMiddlewareAdded)
        {
            app.UseRateLimiter();
        }

        // The administrative routes carry no authentication middleware of their own. That middleware authenticates with
        // the application's default scheme, which belongs to the MCP surface; the authorization middleware instead
        // authenticates with the schemes the policy names, which are this surface's. So requiring the policy is both
        // what admits a caller and what establishes who they are.
        //
        // That is also why this surface's per-caller partition is one bucket rather than one per key: there is no
        // identity to partition on until authorization has run, and running the limiter behind it would serve the
        // guessing this exists to bound. The bucket is the endpoint's, and its capacity is sized as such.
        if (adminEndpointSettings.RequiresAuthentication && !authorizationMiddlewareAdded)
        {
            app.UseAuthorization();
        }

        var adminApi = app.MapAdminApi();

        if (adminRateLimits is not null)
        {
            adminApi.RequireRateLimiting(TransportSurface.Admin.RateLimitingPolicyName);
        }

        if (adminEndpointSettings.RequiresAuthentication)
        {
            adminApi.RequireAuthorization(TransportSurface.Admin.AccessPolicyName);
        }

        if (adminEndpointSettings.AllowsOAuth)
        {
            // Outside the group the requirement was attached to, and deliberately: its reader is a client that has no
            // credential yet and is reading this to find out where to obtain one.
            app.MapAdminProtectedResourceMetadata(adminEndpointSettings.OAuth);
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
