// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Host;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
// Bound strictly: mail transport is security-sensitive, and a misspelled key such as a singular
// "PermittedAuthenticationMechanism" would otherwise be ignored and silently replaced by the default allow-list.
builder.Services.AddOptions<MailSynchronizationOptions>()
    .Bind(
        builder.Configuration.GetSection("MailSynchronization"),
        binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<PersistenceOptions>()
    .Bind(builder.Configuration.GetSection("Persistence"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IImapAccountSettingsProvider>(provider => provider.GetRequiredService<IOptions<MailSynchronizationOptions>>().Value);
builder.Services.AddScoped<IMailTransportSecurityPolicyReader>(provider => provider.GetRequiredService<IOptions<MailSynchronizationOptions>>().Value);
builder.Services.AddScoped(provider =>
{
    var synchronizationOptions = provider.GetRequiredService<IOptions<MailSynchronizationOptions>>().Value;
    return new MailboxSynchronizationOptions
    {
        MaxMetadataBatchSize = synchronizationOptions.MaxMetadataBatchSize,
        MaxRawMimeBytes = synchronizationOptions.MaxRawMimeBytes,
        MaxMetadataBatchesPerRun = synchronizationOptions.MaxMetadataBatchesPerRun,
    };
});
builder.Services.AddSingleton(provider => new PersistenceConcurrencyOptions
{
    MaximumCommitAttempts = provider.GetRequiredService<IOptions<PersistenceOptions>>().Value.MaximumConcurrencyCommitAttempts,
});
builder.Services.AddMailMcpInfrastructure(builder.Configuration);
builder.Services.AddHostedService<MailMcp.Host.Hosting.MailSynchronizationWorker>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

await app.RunAsync();
