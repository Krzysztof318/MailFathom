// Copyright © 2026 Krzysztof Kasprowicz

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "MailMcp", status = "ready" }));

await app.RunAsync();
