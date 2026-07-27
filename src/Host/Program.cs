// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Host;
using MailMcp.Host.Configuration;
using MailMcp.Host.Observability;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Composed before anything else so that a failure in composition, in Build, or in host startup is reported rather
// than only printed. The pipeline the container owns does not exist yet, and on a startup failure it never flushes.
using var bootstrapLogger = BootstrapLogger.Create(builder.Configuration, builder.Environment);
bootstrapLogger.RecordHostStarting();

try
{
    builder.AddServiceDefaults();
    builder.Services.AddProblemDetails();
    builder.Services.AddSingleton(TimeProvider.System);
    // ReferenceOnly is the default, so a deployment that configures nothing gets the mode under which a plain-text value
    // where a reference belongs fails startup instead of authenticating.
    builder.Services.AddSecretResolution(
        builder.Configuration.GetValue("Secrets:Interpretation", SecretValueInterpretation.ReferenceOnly));
    // Bound strictly: mail transport is security-sensitive, and a misspelled key such as a singular
    // "PermittedAuthenticationMechanism" would otherwise be ignored and silently replaced by the default allow-list.
    builder.Services.AddOptions<MailSynchronizationOptions>()
        .Bind(
            builder.Configuration.GetSection("MailSynchronization"),
            binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    // Bound strictly for the same reason as mail transport: a misspelled "Passwrod" would leave the secret block
    // undiscovered, start the host on a passwordless connection string, and surface as an authentication failure later.
    builder.Services.AddOptions<PersistenceOptions>()
        .Bind(
            builder.Configuration.GetSection("Persistence"),
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
    builder.Services.AddSingleton(provider => new PersistenceConcurrencyOptions
    {
        MaximumCommitAttempts = provider.GetRequiredService<IOptions<PersistenceOptions>>().Value.MaximumConcurrencyCommitAttempts,
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
    builder.Services.AddInfrastructure(provider => provider.GetRequiredService<DatabaseConnectionSettingsMapper>()
        .Map(provider.GetRequiredService<ISettingsSnapshot<PersistenceOptions>>().Current));
    builder.Services.AddHostedService<MailMcp.Host.Hosting.MailSynchronizationWorker>();

    var app = builder.Build();

    app.UseExceptionHandler();

    app.MapDefaultEndpoints();
    app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

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
