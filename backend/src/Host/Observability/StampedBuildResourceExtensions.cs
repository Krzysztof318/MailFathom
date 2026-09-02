// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Versioning;
using OpenTelemetry.Resources;

namespace MailFathom.Host.Observability;

/// <summary>Puts the build a process is running onto the resource every record it exports carries.</summary>
/// <remarks>
/// <para>
/// A span, a metric point, and a log record each say what happened. Without the build on the resource none of them says
/// which build it happened in, so a rollout serving two versions at once is attributed by the time a change started
/// rather than by what is running — and a collector holding a week of history cannot be re-read against a build
/// afterwards at all.
/// </para>
/// <para>
/// Two attributes, because the build answers two questions and neither is answered well by the other's value. The
/// version is the compatibility statement an operator groups deployments by, and it carries no source revision, since
/// <see cref="StampedAssemblyVersion" /> splits that off at SemVer's plus sign. The revision is build provenance, which
/// is what turns a report from a deployment the reader did not build into something reproducible, and it reaches
/// nothing else the process exports.
/// </para>
/// <para>
/// Neither attribute is decided by the release channel, because an assembly carries no notion of the channel it was
/// published on. The stamp is what differs: a release build stamps the declared version alone, and a nightly stamps the
/// full prerelease identifier its image tag carries, so the version reported here already tells the two apart without
/// anything having to ask which one this is.
/// </para>
/// <para>
/// The revision is reported as <c>vcs.ref.head.revision</c>, the name OpenTelemetry's attribute registry publishes for a
/// head revision. The <c>service.*</c> namespace publishes nothing for build provenance, and a name invented here would
/// be one no backend recognizes. It reads <c>unknown</c> where the build stamped none — a build with no repository
/// beside it, which the container build is — matching what the startup records report for the same build.
/// </para>
/// <para>
/// Both values are the assembly's own stamp rather than configured ones, which is what keeps a deployment from making
/// its telemetry claim a build the process is not running. They are the same two values, from the same source, that
/// <see cref="BootstrapLogger" /> already reports as properties of the startup records, so the two readings of the build
/// cannot disagree.
/// </para>
/// <para>
/// These two attributes and no more. <c>service.name</c> stays with the OpenTelemetry SDK, which resolves it from
/// <c>OTEL_SERVICE_NAME</c> and falls back to <c>unknown_service:{processName}</c>; naming the service here would agree
/// with the rest of the process only while that variable happened to be set, and would otherwise report one process
/// under two identities. <see cref="BootstrapLogger" /> holds the other half of that arrangement.
/// </para>
/// <para>
/// The attributes are added after whatever the resource has already resolved, and the SDK merges a later resource over
/// an earlier one, so the stamped values win over anything supplied through <c>OTEL_RESOURCE_ATTRIBUTES</c>. That
/// precedence is the decision rather than a consequence of ordering: the build is a fact about the process, and an
/// operator's variable is the one thing that could make it wrong.
/// </para>
/// </remarks>
internal static class StampedBuildResourceExtensions
{
    /// <summary>The OpenTelemetry semantic-convention attribute naming the version of the service a record came from.</summary>
    public const string ServiceVersionAttributeName = "service.version";

    /// <summary>The OpenTelemetry attribute naming the source revision the service a record came from was built at.</summary>
    public const string SourceRevisionAttributeName = "vcs.ref.head.revision";

    /// <summary>Adds the version and the source revision this assembly was stamped with to a resource being composed.</summary>
    /// <param name="resource">The resource builder being composed.</param>
    /// <returns>The same builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource" /> is <see langword="null" />.</exception>
    public static ResourceBuilder AddStampedBuildIdentity(this ResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var stamped = StampedBuild;

        return resource.AddAttributes(
        [
            KeyValuePair.Create(ServiceVersionAttributeName, (object)stamped.Version),
            KeyValuePair.Create(SourceRevisionAttributeName, (object)stamped.Revision),
        ]);
    }

    /// <summary>The version the host assembly was stamped with at build time, <c>unknown</c> when the build stamped none.</summary>
    /// <remarks>
    /// This assembly rather than the entry assembly, for the reason <see cref="BootstrapLoggingSettings" /> reads its
    /// own: the reported identity is the host's under every process that loads it, including the test runner.
    /// </remarks>
    internal static string StampedServiceVersion => StampedBuild.Version;

    /// <summary>The source revision the host assembly was built at, <c>unknown</c> when the build stamped none.</summary>
    internal static string StampedSourceRevision => StampedBuild.Revision;

    private static StampedAssemblyVersion StampedBuild => StampedAssemblyVersion.ReadFrom(HostAssembly);

    private static Assembly HostAssembly => typeof(StampedBuildResourceExtensions).Assembly;
}
