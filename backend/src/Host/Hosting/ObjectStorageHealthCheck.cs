// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.ObjectStorage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting;

/// <summary>Asks the configured object-storage bucket whether it is reachable, readable, and writable, on every readiness scrape.</summary>
/// <remarks>
/// <para>
/// A bucket's availability is a readiness fact rather than a startup one. It may become reachable after this process
/// does, its credential may be rotated out from under a running deployment, and a policy change can leave it readable
/// and no longer writable — so a check that ran once while the host came up establishes nothing about any of it. That is
/// also what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1
/// means by making an endpoint that has been taken away a readiness condition rather than a configuration error a binder
/// could catch: whether object-backed rows exist depends on the database, and startup must not query one.
/// </para>
/// <para>
/// <b>It reports unhealthy rather than degraded.</b> An instance whose selected content backend cannot be written to
/// cannot store the next message it synchronizes, and cannot read the ones it already put there; it is not serving a
/// narrower service, it is failing the thing mail is stored in. Reporting anything better would keep it in the load
/// balancer to answer requests it is about to fail.
/// </para>
/// <para>
/// It carries the readiness tag alone and must never reach the liveness probe. Restarting this process cannot make a
/// bucket reachable, and a liveness failure would turn one endpoint's outage into a restart loop across every replica.
/// </para>
/// <para>
/// <b>The log is where the reason lives.</b> A probe response is one word by design — it is served without a credential,
/// so a description would disclose which dependencies exist and what is wrong with one — which leaves an operator with a
/// <c>503</c> and nothing to read. So a transition into unavailability is logged at <c>Error</c> with the classified
/// failure behind it, and the recovery is logged in turn. The transition is logged rather than the observation, because
/// a ten-second readiness period would otherwise bury the record that says when an outage began under six copies of
/// itself a minute; that is why the last observation is held here, and why every read and write of it is taken under the
/// same lock, since scrapes arrive on whichever thread served them.
/// </para>
/// </remarks>
internal sealed partial class ObjectStorageHealthCheck : IHealthCheck
{
    /// <summary>The name the check is registered under.</summary>
    internal const string Name = "object-storage";

    private readonly Lock mutex = new();
    private readonly IObjectStorageEndpointProbe probe;
    private readonly ILogger<ObjectStorageHealthCheck> logger;

    private BucketAvailability lastObserved = BucketAvailability.Unobserved;

    /// <summary>Initializes a new object-storage health check.</summary>
    /// <param name="probe">Asks the configured bucket for a listing, a write, and the removal of what was written.</param>
    /// <param name="logger">Reports the two transitions this check exists to make visible.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ObjectStorageHealthCheck(
        IObjectStorageEndpointProbe probe,
        ILogger<ObjectStorageHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(logger);

        this.probe = probe;
        this.logger = logger;
    }

    /// <summary>What this check has established about the bucket so far.</summary>
    private enum BucketAvailability
    {
        /// <summary>No scrape has reached the bucket yet.</summary>
        Unobserved = 0,

        /// <summary>The last scrape listed, wrote, and removed.</summary>
        Available = 1,

        /// <summary>The last scrape did not.</summary>
        Unavailable = 2,
    }

    /// <summary>Builds the registration this check is added to the health-check service through.</summary>
    /// <returns>The registration, carrying the name, the readiness tag, and the status a failure reports.</returns>
    /// <remarks>
    /// Built here rather than at the call site, so the three decisions that make this check what it is — that it reaches
    /// the readiness probe alone, that it is called what a report names it, and that a failure is unhealthy rather than
    /// degraded — are made in the file that explains them.
    /// </remarks>
    internal static HealthCheckRegistration Registration() => new(
        Name,
        static provider => provider.GetRequiredService<ObjectStorageHealthCheck>(),
        HealthStatus.Unhealthy,
        [HealthProbe.Readiness.Tag]);

    /// <inheritdoc />
    /// <remarks>
    /// Cancellation propagates rather than being reported: a scrape the caller abandoned is a fact about the caller, and
    /// answering unhealthy for it would take an instance out of traffic over a request that was never completed.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever stopped the probe, the bucket did not answer, and an operator needs the reason in the log rather than an unhealthy verdict with nothing behind it.")]
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await this.probe.VerifyAvailableAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            if (this.Observed(BucketAvailability.Unavailable))
            {
                this.LogBucketUnavailable(failure);
            }

            return HealthCheckResult.Unhealthy(
                "The configured object-storage bucket did not answer, so this instance can neither store nor read the message content it holds there.");
        }

        if (this.Observed(BucketAvailability.Available))
        {
            this.LogBucketAvailable();
        }

        return HealthCheckResult.Healthy("The configured object-storage bucket is reachable, readable, and writable.");
    }

    /// <summary>Records what this scrape saw, and reports whether it changed what the previous one saw.</summary>
    /// <remarks>The first observation of either kind is a transition, so an instance that comes up with no bucket says so once rather than staying silent because nothing changed.</remarks>
    private bool Observed(BucketAvailability availability)
    {
        lock (this.mutex)
        {
            if (this.lastObserved == availability)
            {
                return false;
            }

            this.lastObserved = availability;

            return true;
        }
    }

    /// <summary>
    /// Reports the bucket as the reason this instance is unready. The failure is passed whole because it is the only
    /// account of what went wrong; MailFathom's own message names the configuration key rather than the address, for the
    /// reason <see cref="ObjectStorageUnavailableException" /> records. The wording is state-neutral because this record
    /// covers a bucket that has never answered as well as one that has stopped.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The configured object-storage bucket is not answering, so this instance reports unready and can neither store nor read message content. It is not restarted, and it becomes ready by itself once the bucket answers: check the endpoint, the credential, and the bucket policy named under ContentStorage:ObjectStorage.")]
    private partial void LogBucketUnavailable(Exception failure);

    /// <summary>Reports the recovery, which is what tells an operator watching the first record that the outage ended.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The configured object-storage bucket is reachable, readable, and writable, so this instance reports ready.")]
    private partial void LogBucketAvailable();
}
