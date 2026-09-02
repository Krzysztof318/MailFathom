// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace MailFathom.Infrastructure;

/// <summary>Infrastructure registration that needs the host builder rather than the service collection alone.</summary>
[RequiresIntegrationCoverage]
public static class HostApplicationBuilderExtensions
{
    /// <summary>The seconds a database command may run before it is cancelled, when no deployment configures another value.</summary>
    public const int DefaultDatabaseCommandTimeoutSeconds = 30;

    /// <summary>The name the database health check is registered under, which is the context type's own name.</summary>
    /// <remarks>The enrichment names it, so this restates that name rather than choosing one. It is the registration the configured probe tags are applied to.</remarks>
    private const string DatabaseHealthCheckName = nameof(MailFathomDbContext);

    /// <summary>Makes the EF Core context report its health and publish database traces and metrics.</summary>
    /// <param name="builder">The host application builder the context is already registered on.</param>
    /// <param name="commandTimeout">How long a single database command may run before it is cancelled.</param>
    /// <param name="probeTags">The health-check tags that decide which probes consult the database, stated by the composition root that owns the probes.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder" /> or <paramref name="probeTags" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="commandTimeout" /> is not a positive duration.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the context has not been registered yet.</exception>
    /// <remarks>
    /// <para>
    /// This is deliberately the enrichment half of the Aspire PostgreSQL EF Core integration rather than its
    /// <c>AddNpgsqlDbContext</c> half. That method resolves a connection string from
    /// <c>ConnectionStrings</c> at registration time and builds the context around it, which MailFathom cannot use: the
    /// connection string is composed asynchronously during startup because resolving a secret reference is asynchronous,
    /// and the password is supplied per physical connection so a rotated credential needs no restart. Enrichment layers
    /// the health check, the tracing, and the metrics onto the context the infrastructure already registered, so a
    /// deployment that runs without any orchestrator — reading <c>Persistence:ConnectionString</c> or
    /// <c>Persistence:Password</c> from its own secret store — gets the same telemetry as one Aspire starts.
    /// </para>
    /// <para>
    /// Retries stay off. A retrying execution strategy refuses the user-initiated transaction
    /// <see cref="PersistenceSessionFactory" /> opens for every session, so enabling it would fail every write at
    /// session start rather than merely leave it un-retried, and it would nest inside the outbound resilience pipeline
    /// that already governs database command execution. Adopting retries instead means handing each unit of work to
    /// <c>Database.CreateExecutionStrategy().ExecuteAsync</c> and dropping that pipeline from the same paths.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddDatabaseHealthAndTelemetry(
        this IHostApplicationBuilder builder,
        TimeSpan commandTimeout,
        IReadOnlyCollection<string> probeTags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(probeTags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(commandTimeout, TimeSpan.Zero);

        // Published so a reloaded candidate can be compared against the value the context was actually built with. It
        // is written into the context options here and nothing reapplies it, so a reload that changed it would
        // otherwise be reported as adopted while every command kept the old bound.
        builder.Services.AddSingleton(new DatabaseCommandTimeout(commandTimeout));

        builder.EnrichNpgsqlDbContext<MailFathomDbContext>(settings =>
        {
            settings.DisableRetry = true;
            settings.DisableHealthChecks = false;
            settings.DisableTracing = false;
            settings.DisableMetrics = false;
            settings.CommandTimeout = (int)commandTimeout.TotalSeconds;
        });

        // The enrichment registers the check without tags, and a probe selects its checks by tag, so an untagged check
        // reaches no probe. Which probes the database belongs to is not this layer's decision — it is the composition
        // root's — but the registration is here, which is where the answer has to be applied.
        builder.Services.Configure<HealthCheckServiceOptions>(healthCheckOptions =>
        {
            var databaseRegistrations = healthCheckOptions.Registrations
                .Where(static registration => string.Equals(registration.Name, DatabaseHealthCheckName, StringComparison.Ordinal));

            foreach (var registration in databaseRegistrations)
            {
                foreach (var tag in probeTags)
                {
                    registration.Tags.Add(tag);
                }
            }
        });

        return builder;
    }
}
