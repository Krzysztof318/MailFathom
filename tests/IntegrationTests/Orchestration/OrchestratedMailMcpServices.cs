// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Infrastructure;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Resilience;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailMcp.IntegrationTests.Orchestration;

/// <summary>The production registrations, resolved against the orchestrated database and mail server.</summary>
/// <remarks>
/// <para>
/// A real host rather than a bare service provider, because infrastructure composes the connection string during
/// hosted-service startup so that resolving a secret reference stays asynchronous. Everything MailMcp itself
/// registers comes from <see cref="ServiceCollectionExtensions.AddInfrastructure" />, so what a test exercises is the
/// wiring a deployment gets; only the inputs a composition root binds from configuration are supplied here, because the
/// suite deliberately does not start the host resource.
/// </para>
/// <para>
/// Batching is left at values a test can reason about rather than at production defaults: one run must be able to
/// consume a seeded mailbox whole, so a checkpoint a later test reads describes the whole folder rather than the first
/// page of it.
/// </para>
/// </remarks>
internal sealed class OrchestratedMailMcpServices : IAsyncDisposable
{
    private readonly IHost host;

    private OrchestratedMailMcpServices(IHost host) => this.host = host;

    /// <summary>Starts the composed services against the orchestrated infrastructure.</summary>
    /// <param name="orchestration">The running orchestration whose database and mail server are used.</param>
    /// <param name="cancellationToken">Cancels the startup.</param>
    /// <returns>The composed services, which the caller owns and must dispose.</returns>
    internal static async Task<OrchestratedMailMcpServices> StartAsync(
        MailMcpOrchestrationFixture orchestration,
        CancellationToken cancellationToken)
    {
        var builder = new HostApplicationBuilder();
        var account = new SyntheticMailAccount(orchestration.MailServer);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
        builder.Services.AddSingleton<IImapAccountSettingsProvider>(account);
        builder.Services.AddSingleton<IMailTransportSecurityPolicyReader>(account);
        builder.Services.AddSingleton(new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 50,
            MaxRawMimeBytes = 1024L * 1024L,
            MaxMetadataBatchesPerRun = 10,
        });
        builder.Services.AddSingleton(new EmailMimeExtractionOptions
        {
            MaxPartCount = 100,
            MaxNestingDepth = 10,
            MaxExtractedTextCharacters = 10_000,
        });
        builder.Services.AddSingleton(new PersistenceConcurrencyOptions { MaximumCommitAttempts = 3 });
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default);

        var host = builder.Build();
        await host.StartAsync(cancellationToken);

        return new OrchestratedMailMcpServices(host);
    }

    /// <summary>Runs one unit of work in its own dependency-injection scope, the way a worker does.</summary>
    /// <typeparam name="TResult">What the unit of work produces.</typeparam>
    /// <param name="work">The unit of work.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the unit of work produced.</returns>
    internal async Task<TResult> InScopeAsync<TResult>(
        Func<IServiceProvider, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        await using var scope = this.host.Services.CreateAsyncScope();

        return await work(scope.ServiceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.host.StopAsync(CancellationToken.None);
        this.host.Dispose();
    }
}
