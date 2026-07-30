// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Host;
using MailMcp.Host.Configuration;
using MailMcp.Host.Observability;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Resilience;
using MailMcp.Infrastructure.Secrets;
using MailMcp.Mcp;
using Microsoft.Extensions.Options;

// Composed before anything else, CreateBuilder included, so that a malformed appsettings.json, a failure during
// composition, and a failed host start are all reported rather than only printed. The pipeline the container owns
// does not exist until Build has returned, and on a startup failure it never flushes.
using var bootstrapLogger = BootstrapLogger.CreateFromEnvironment();
bootstrapLogger.RecordHostStarting();

try
{
    var builder = WebApplication.CreateBuilder(args);

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
    // The validator is registered ahead of the worker so hosted-service ordering reinforces the StartingAsync ordering
    // rather than depending on it alone, and ahead of the infrastructure so an operator who mistyped several references
    // reads one aggregated report rather than whichever failure the database happened to hit first.
    builder.Services.AddHostedService<MailMcp.Host.Hosting.SecretConfigurationStartupValidator>();
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
    builder.AddDatabaseHealthAndTelemetry(TimeSpan.FromSeconds(builder.Configuration.GetValue(
        "Persistence:CommandTimeoutSeconds",
        HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)));
    // Ahead of the workers so no unit of work reads or writes mail before the schema this build expects is proven, and
    // after the infrastructure that registers the inspector it resolves.
    builder.Services.AddHostedService<MailMcp.Host.Hosting.DatabaseSchemaStartupGate>();
    builder.Services.AddHostedService<MailMcp.Host.Hosting.MailSynchronizationWorker>();
    builder.Services.AddHostedService<MailMcp.Host.Hosting.MailExtractionBackfillWorker>();
    // Registered whether or not the endpoint is enabled, because it is the warning that decides whether it has anything
    // to say. Registering it conditionally would put the same condition in two places.
    builder.Services.AddHostedService<MailMcp.Host.Hosting.McpTransportAuthenticationWarning>();

    // Read once and registered, so the value that decides the route is the one every consumer resolves. Whether the
    // endpoint exists is decided while the application is being built, before a container that could resolve a snapshot
    // exists, and a second read of a reloadable source could otherwise map the endpoint from one value while the missing
    // authentication was warned about from another.
    //
    // Bound strictly like the other security-sensitive sections: a misspelled "Enabeld" would leave the endpoint off
    // while an operator believed they had turned it on.
    var mcpEndpointSettings = builder.Configuration
        .GetSection(McpEndpointOptions.SectionName)
        .Get<McpEndpointOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        ?? new McpEndpointOptions();
    builder.Services.AddSingleton(Options.Create(mcpEndpointSettings));

    if (mcpEndpointSettings.Enabled)
    {
        // The tools read the local mailbox copy through the use cases the infrastructure registration above already
        // added, so the protocol surface adds no port of its own.
        builder.Services.AddMailMcpServer();
    }

    var app = builder.Build();

    app.UseExceptionHandler();

    app.MapDefaultEndpoints();
    app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

    if (mcpEndpointSettings.Enabled)
    {
        app.MapMcp(McpEndpointRoute.Path);
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
