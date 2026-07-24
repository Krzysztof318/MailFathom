// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail.MailKit;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<MailMcp.Host.Configuration.MailSynchronizationOptions>()
    .Bind(builder.Configuration.GetSection("MailSynchronization"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IMailKitImapAccountSettingsProvider>(provider => provider.GetRequiredService<IOptions<MailSynchronizationOptions>>().Value);
builder.Services.AddMailMcpInfrastructure(builder.Configuration);
builder.Services.AddHostedService<MailMcp.Host.Hosting.MailSynchronizationWorker>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

await app.RunAsync();
