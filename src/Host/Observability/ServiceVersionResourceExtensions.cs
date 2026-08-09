// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Versioning;
using OpenTelemetry.Resources;

namespace MailFathom.Host.Observability;

/// <summary>Puts the build a process is running onto the resource every record it exports carries.</summary>
/// <remarks>
/// <para>
/// A span, a metric point, and a log record each say what happened. Without <c>service.version</c> on the resource none
/// of them says which build it happened in, so a rollout serving two versions at once is attributed by the time a
/// change started rather than by what is running — and a collector holding a week of history cannot be re-read against
/// a build afterwards at all.
/// </para>
/// <para>
/// The value is the assembly's own stamped version rather than a configured one, which is what keeps a deployment from
/// making its telemetry claim a build the process is not running. It is the same value, from the same source, that the
/// startup records already report as a property, so the two readings of the version cannot disagree.
/// </para>
/// <para>
/// One attribute and no more. <c>service.name</c> stays with the OpenTelemetry SDK, which resolves it from
/// <c>OTEL_SERVICE_NAME</c> and falls back to <c>unknown_service:{processName}</c>; naming the service here would agree
/// with the rest of the process only while that variable happened to be set, and would otherwise report one process
/// under two identities. <see cref="BootstrapLogger" /> holds the other half of that arrangement.
/// </para>
/// <para>
/// The attribute is added after whatever the resource has already resolved, and the SDK merges a later resource over an
/// earlier one, so the stamped version wins over a <c>service.version</c> supplied through
/// <c>OTEL_RESOURCE_ATTRIBUTES</c>. That precedence is the decision rather than a consequence of ordering: the build is
/// a fact about the process, and an operator's variable is the one thing that could make it wrong.
/// </para>
/// </remarks>
internal static class ServiceVersionResourceExtensions
{
    /// <summary>The OpenTelemetry semantic-convention attribute naming the version of the service a record came from.</summary>
    public const string ServiceVersionAttributeName = "service.version";

    /// <summary>Adds the version this assembly was stamped with to a resource being composed.</summary>
    /// <param name="resource">The resource builder being composed.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource" /> is <see langword="null" />.</exception>
    public static ResourceBuilder AddStampedServiceVersion(this ResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.AddAttributes(
            [KeyValuePair.Create(ServiceVersionAttributeName, (object)StampedServiceVersion)]);
    }

    /// <summary>The version the host assembly was stamped with at build time, <c>unknown</c> when the build stamped none.</summary>
    /// <remarks>
    /// This assembly rather than the entry assembly, for the reason <see cref="BootstrapLoggingSettings" /> reads its
    /// own: the reported identity is the host's under every process that loads it, including the test runner.
    /// </remarks>
    internal static string StampedServiceVersion => StampedAssemblyVersion.ReadFrom(HostAssembly).Version;

    private static Assembly HostAssembly => typeof(ServiceVersionResourceExtensions).Assembly;
}
