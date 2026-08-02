// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.UnitTests;

/// <summary>A health check that reports the status it was created with.</summary>
/// <remarks>
/// The probes select checks by tag and report what those checks answered, so what a test needs from a check is a status
/// it decides and a registration it can tag. Substituting the interface would say the same thing through a mock's
/// configuration, and the real registration type is what the probe predicates read.
/// </remarks>
internal sealed class StubHealthCheck : IHealthCheck
{
    private readonly HealthStatus status;

    internal StubHealthCheck(HealthStatus status) => this.status = status;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new HealthCheckResult(this.status));

    /// <summary>Registers a check under a name, a status, and the probe tags it belongs to.</summary>
    internal static HealthCheckRegistration Registration(string name, HealthStatus status, params string[] tags) =>
        new(name, new StubHealthCheck(status), failureStatus: null, tags);
}
