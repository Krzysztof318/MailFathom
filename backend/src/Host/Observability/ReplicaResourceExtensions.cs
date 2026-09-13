// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using OpenTelemetry.Resources;

namespace MailFathom.Host.Observability;

/// <summary>Puts the replica a process is onto the resource every record it exports carries.</summary>
/// <remarks>
/// <para>
/// Several replicas export under one service name and one build, so without an instance attribute a backend that derives
/// a series from the resource receives cumulative points from every replica into one series, and a log record or a span
/// cannot be filtered to the process that produced it. <c>service.instance.id</c> is the name OpenTelemetry's semantic
/// conventions publish for that, and it is what Prometheus and Grafana map to <c>instance</c>.
/// </para>
/// <para>
/// The value is the <see cref="ReplicaIdentity" /> the host registers, so the replica an administrative answer or a
/// <c>work_leases</c> row names as a holder is the value its telemetry is filtered on.
/// </para>
/// <para>
/// Unlike the build, the replica gives way to an operator: a <c>service.instance.id</c> written into
/// <c>OTEL_RESOURCE_ATTRIBUTES</c> wins, because how instances are named is a fact about the deployment rather than about
/// the process. The SDK merges a later resource over an earlier one and <c>CreateDefault</c> has already read the
/// variable, so the environment detectors are added again after the replica — in the order the SDK adds them, which keeps
/// <c>OTEL_SERVICE_NAME</c> ahead of a service name in the variable. Anything added after this call, the stamped build
/// among it, still wins over both.
/// </para>
/// </remarks>
internal static class ReplicaResourceExtensions
{
    /// <summary>The OpenTelemetry semantic-convention attribute naming the instance of the service a record came from.</summary>
    public const string ServiceInstanceIdAttributeName = "service.instance.id";

    /// <summary>Adds the replica this process is to a resource being composed, below anything the environment supplies.</summary>
    /// <param name="resource">The resource builder being composed.</param>
    /// <param name="replica">The replica the host registers.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource" /> or <paramref name="replica" /> is <see langword="null" />.</exception>
    public static ResourceBuilder AddReplicaIdentity(this ResourceBuilder resource, ReplicaIdentity replica)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(replica);

        return resource
            .AddAttributes([KeyValuePair.Create(ServiceInstanceIdAttributeName, (object)replica.Value)])
            .AddEnvironmentVariableDetector();
    }
}
