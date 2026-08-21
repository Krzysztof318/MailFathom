// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting;

/// <summary>Reports what the last call to one declared AI provider established about it.</summary>
/// <remarks>
/// <para>
/// One registration per declared provider rather than one covering both, which is what makes the two states
/// independently observable: each carries its own check name, so a probe report and the health-check metrics tell an
/// unreachable chat provider from an unreachable embedding provider without either hiding the other. A deployment that
/// declared only one registers only one.
/// </para>
/// <para>
/// It never reports unhealthy, and that is the posture rather than a softened verdict. Both providers are optional and
/// neither serves a request path: an instance with a failing embedding provider still answers every search lexically,
/// and an instance with a failing chat provider still answers every search at all. Reporting unhealthy on the readiness
/// probe would take an instance out of traffic for a capability the traffic was not asking for, and reporting it on the
/// liveness probe would restart a process that is working.
/// </para>
/// <para>
/// It consults process-local state and calls no provider. A health check that made a paid request would spend an
/// operator's money on every scrape and would report on a call nobody asked for.
/// </para>
/// </remarks>
internal sealed class AiProviderHealthCheck : IHealthCheck
{
    private readonly IAiProviderHealthReader healthReader;
    private readonly AiProviderRole role;

    /// <summary>Initializes a check over one provider role.</summary>
    /// <param name="healthReader">The state every provider call reports into.</param>
    /// <param name="role">Which provider this check answers for.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="healthReader" /> is <see langword="null" />.</exception>
    public AiProviderHealthCheck(IAiProviderHealthReader healthReader, AiProviderRole role)
    {
        ArgumentNullException.ThrowIfNull(healthReader);

        this.healthReader = healthReader;
        this.role = role;
    }

    /// <summary>Names the check a declared provider is registered under.</summary>
    /// <param name="role">The provider role.</param>
    /// <returns>The registration name.</returns>
    /// <remarks>Distinct per role, because the name is the only thing that tells two otherwise identical checks apart wherever a report lists them.</remarks>
    internal static string NameOf(AiProviderRole role) => role switch
    {
        AiProviderRole.Embedding => "ai-embedding-provider",
        AiProviderRole.Chat => "ai-chat-provider",
        _ => "ai-provider",
    };

    /// <summary>Builds the registration one declared provider is added to the health-check service through.</summary>
    /// <param name="role">The provider role.</param>
    /// <returns>The registration, carrying the name, the probe tag, and the status a thrown check reports.</returns>
    /// <remarks>
    /// Built here rather than at each call site, so the three decisions that make this check what it is — which probe it
    /// reaches, what it is called, and that it never reports worse than degraded — are made once for both roles.
    /// </remarks>
    internal static HealthCheckRegistration RegistrationFor(AiProviderRole role) => new(
        NameOf(role),
        provider => new AiProviderHealthCheck(provider.GetRequiredService<IAiProviderHealthReader>(), role),
        HealthStatus.Degraded,
        [HealthProbe.Readiness.Tag]);

    /// <inheritdoc />
    /// <remarks>
    /// A provider nothing has called yet reports healthy. It has failed at nothing, and a freshly started instance whose
    /// first unit of work has not arrived is the ordinary case rather than a condition to page somebody about.
    /// </remarks>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = this.healthReader.Read(this.role);

        return Task.FromResult(health.State switch
        {
            AiProviderHealthState.Unobserved => HealthCheckResult.Healthy(
                "No call has been made to this provider yet."),
            AiProviderHealthState.Serving => HealthCheckResult.Healthy(
                "The last call to this provider produced an answer."),
            AiProviderHealthState.Unavailable => HealthCheckResult.Degraded(
                "The last call to this provider failed for a reason a later attempt may not meet."),
            _ => HealthCheckResult.Degraded(
                "The last call to this provider failed for a reason no later attempt changes; a credential or a declaration needs correcting."),
        });
    }
}
