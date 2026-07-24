// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail.MailKit;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<MailSynchronizationOptions>()
    .Bind(builder.Configuration.GetSection("MailSynchronization"), options => options.ErrorOnUnknownConfiguration = true)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<MailSynchronizationSettingsReader>();
builder.Services.AddSingleton<ISynchronizationSettingsReader>(provider => provider.GetRequiredService<MailSynchronizationSettingsReader>());
builder.Services.AddSingleton<IMailKitImapAccountSettingsProvider>(provider => provider.GetRequiredService<MailSynchronizationSettingsReader>());
builder.Services.AddScoped(provider => provider.GetRequiredService<ISynchronizationSettingsReader>().GetCurrentSettings().Limits);
builder.Services.AddMailMcpInfrastructure(builder.Configuration);
builder.Services.AddHostedService<MailMcp.Host.Hosting.MailSynchronizationWorker>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

await app.RunAsync();
