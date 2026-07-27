// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Host;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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
        .FindSecretReferenceErrorsAsync("Persistence", candidate, cancellationToken),
    "Persistence",
    provider.GetRequiredService<ILogger<ValidatedSettingsSnapshot<PersistenceOptions>>>()));
builder.Services.AddSingleton<ISettingsSnapshot<MailSynchronizationOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<MailSynchronizationOptions>>());
builder.Services.AddSingleton<ISettingsSnapshot<PersistenceOptions>>(provider => provider.GetRequiredService<ValidatedSettingsSnapshot<PersistenceOptions>>());
// One work unit captures the snapshot once, so the transport security policy it validates against and the material it
// connects with can never come from two different reloads.
builder.Services.AddScoped(provider => provider.GetRequiredService<ISettingsSnapshot<MailSynchronizationOptions>>().Current);
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
// The connection string comes from ordinary configuration, read once, because it names which database this is. The
// secret blocks come from the published snapshot on every read, so a reference an operator repoints reaches the next
// physical connection instead of waiting for a restart.
var configuredConnectionString = builder.Configuration.GetConnectionString("mailmcp");
builder.Services.AddInfrastructure(provider =>
{
    var persistenceSettings = provider.GetRequiredService<ISettingsSnapshot<PersistenceOptions>>().Current;

    return new PostgresConnectionSettings(
        configuredConnectionString,
        persistenceSettings.ConnectionString,
        persistenceSettings.Password);
});
builder.Services.AddHostedService<MailMcp.Host.Hosting.MailSynchronizationWorker>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

await app.RunAsync();
