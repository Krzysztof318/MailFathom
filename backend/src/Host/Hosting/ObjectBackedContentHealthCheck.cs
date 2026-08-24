// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting;

/// <summary>Reports a deployment that holds mail in an object endpoint it is no longer configured to reach.</summary>
/// <remarks>
/// <para>
/// A content row names the store holding its payload, so a deployment that selected the object backend, stored
/// messages there, and then lost the configuration keeps every one of those rows intact and unreadable. Nothing else
/// notices: the mailbox answers, the timeline answers, the metadata answers, and the failure arrives only when
/// somebody asks for the content of one of those particular messages. This is the check that says so first.
/// </para>
/// <para>
/// It is registered only where nothing else covers the question — on a deployment that named no endpoint at all.
/// Where one is named, <see cref="ObjectStorageHealthCheck" /> asks it for a listing, a write, and a removal on every
/// scrape, which answers both halves of the same operator question and answers them about the endpoint rather than
/// about the configuration.
/// </para>
/// <para>
/// <b>The question is asked of the database and therefore not at startup.</b>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 1
/// makes an endpoint that has been taken away a readiness condition for exactly this reason: whether such rows exist
/// is a fact about stored mail, and a host that queried for it while composing would refuse to start against a
/// database no migration had reached yet. A read that fails is reported as this check failing rather than answered as
/// an absence, because a database nothing can be read from is not a deployment holding no object-backed content.
/// </para>
/// <para>
/// <b>It reports unhealthy rather than degraded</b>, for the reason <see cref="ObjectStorageHealthCheck" /> gives: an
/// instance that cannot read the mail it holds is failing the thing mail is stored in rather than serving a narrower
/// service. It carries the readiness tag alone and must never reach the liveness probe — restarting this process
/// cannot restore a configuration key, and a liveness failure would turn one missing section into a restart loop.
/// </para>
/// <para>
/// <b>The transition is logged rather than the observation</b>, and the last one is held here under a lock, for the
/// reason that check records: a ten-second readiness period would otherwise bury the record that says when the
/// condition began under six copies of itself a minute, and scrapes arrive on whichever thread served them.
/// </para>
/// </remarks>
internal sealed partial class ObjectBackedContentHealthCheck : IHealthCheck
{
    /// <summary>The name the check is registered under.</summary>
    internal const string Name = "object-backed-content";

    private readonly Lock mutex = new();
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<ObjectBackedContentHealthCheck> logger;

    private ContentReachability lastObserved = ContentReachability.Unobserved;

    /// <summary>Initializes a new object-backed content health check.</summary>
    /// <param name="scopeFactory">Opens the scope the inventory is read in, because this check outlives one and the inventory does not.</param>
    /// <param name="logger">Reports the two transitions this check exists to make visible.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ObjectBackedContentHealthCheck(
        IServiceScopeFactory scopeFactory,
        ILogger<ObjectBackedContentHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    /// <summary>What this check has established about the stored content so far.</summary>
    private enum ContentReachability
    {
        /// <summary>No scrape has read the inventory yet.</summary>
        Unobserved = 0,

        /// <summary>The last scrape found no payload held anywhere this deployment cannot reach.</summary>
        Reachable = 1,

        /// <summary>The last scrape found payloads held in an object endpoint this deployment names none of.</summary>
        Stranded = 2,
    }

    /// <summary>Builds the registration this check is added to the health-check service through.</summary>
    /// <returns>The registration, carrying the name, the readiness tag, and the status a failure reports.</returns>
    /// <remarks>Built here rather than at the call site, so the decisions the remarks above explain are made in the file that explains them.</remarks>
    internal static HealthCheckRegistration Registration() => new(
        Name,
        static provider => provider.GetRequiredService<ObjectBackedContentHealthCheck>(),
        HealthStatus.Unhealthy,
        [HealthProbe.Readiness.Tag]);

    /// <inheritdoc />
    /// <remarks>
    /// A read that throws is left to propagate, which the registration above reports as unhealthy with the failure
    /// recorded. Cancellation propagates too rather than being reported: a scrape the caller abandoned is a fact about
    /// the caller, and answering unhealthy for it would take an instance out of traffic over a request that was never
    /// completed.
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = this.scopeFactory.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IObjectBackedContentInventory>();

        if (!await inventory.HoldsObjectBackedContentAsync(cancellationToken))
        {
            if (this.Observed(ContentReachability.Reachable))
            {
                this.LogContentReachable();
            }

            return HealthCheckResult.Healthy("Every stored payload is held where this instance can read it.");
        }

        if (this.Observed(ContentReachability.Stranded))
        {
            this.LogContentStranded();
        }

        return HealthCheckResult.Unhealthy(
            "Message content is held in an object endpoint this instance is configured to reach none of, so those messages cannot be read.");
    }

    /// <summary>Records what this scrape saw, and reports whether it changed what the previous one saw.</summary>
    /// <remarks>The first observation of either kind is a transition, so an instance that comes up stranded says so once rather than staying silent because nothing changed.</remarks>
    private bool Observed(ContentReachability reachability)
    {
        lock (this.mutex)
        {
            if (this.lastObserved == reachability)
            {
                return false;
            }

            this.lastObserved = reachability;

            return true;
        }
    }

    /// <summary>
    /// Reports the stranded content as the reason this instance is unready. The remedy is a configuration key rather
    /// than anything an operator can do to this process, which is what the record says instead of naming an address the
    /// probe response deliberately does not disclose.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Stored message content is held in an object endpoint, and this instance names none, so it reports unready and cannot read that mail. It is not restarted, and it becomes ready by itself once the endpoint is configured again: restore the block under ContentStorage:ObjectStorage that this deployment stored the content through.")]
    private partial void LogContentStranded();

    /// <summary>Reports the recovery, which is what tells an operator watching the first record that the condition ended.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No stored message content is held in an object endpoint this instance cannot reach, so it reports ready.")]
    private partial void LogContentReachable();
}
