// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailFathom.Host.Hosting;

/// <summary>Asks the personal-data analyzer whether it can answer, on every readiness scrape, for as long as the process runs.</summary>
/// <remarks>
/// <para>
/// The analyzer is a sidecar, and a sidecar's availability is a readiness fact rather than a startup one. It may become
/// ready after this process does, and it may stop answering hours later; a check that ran once while the host came up
/// establishes nothing about either. So this reaches the analyzer on every scrape rather than reporting a flag the way
/// the startup-gate check does, which is the difference between the two probes: one asks whether the host finished
/// coming up, this asks whether it can serve right now.
/// </para>
/// <para>
/// <b>It reports unhealthy, where the AI provider checks never report worse than degraded.</b> That is the fail-closed
/// contract read from the other side: with the personal-data scanner switched on, an instance whose analyzer cannot
/// answer refuses every read, derived write, and egress the scanner guards, so it is not serving a narrower service —
/// it is serving nothing the scanner covers. Reporting anything better would keep an instance in the load balancer to
/// answer requests it has already decided to refuse.
/// </para>
/// <para>
/// <b>It asks whether anybody is scanned for before it asks the analyzer.</b> The scanner is switched on per owner, so
/// a deployment that stood the analyzer up without scanning its own owners' mail with it is not made unready by that
/// analyzer's silence — nothing is being refused — while one where a single owner switched it on for their own mail is.
/// Registration cannot decide this, because it happens before the roster the answer is composed from exists.
/// </para>
/// <para>
/// It carries the readiness tag alone and must never reach the liveness probe. Restarting this process cannot start a
/// container beside it, and a liveness failure would turn one sidecar's outage into a restart loop across every replica.
/// </para>
/// <para>
/// <b>The log is where the reason lives.</b> A probe response is one word by design — it is served without a credential,
/// so a description would disclose which dependencies exist and what is wrong with one — which leaves an operator with
/// a `503` and nothing to read. So a transition into unavailability is logged at <c>Error</c> with the failure behind
/// it, and the recovery is logged in turn. Neither record claims the analyzer <i>stopped</i>: the first scrape of a
/// process whose sidecar is still loading its model is the same transition as an outage, and a message written for the
/// second would misreport an ordinary cold start as one.
/// </para>
/// <para>
/// It logs the transition rather than the observation. A readiness period of ten seconds would otherwise write six
/// records a minute for as long as the outage lasted, which buries the record that says when it began under copies of
/// itself; what an operator needs from a log is when the analyzer stopped serving this instance and when it began. The last
/// observation is therefore held here, which is why this check is a singleton — the registration resolves the one
/// instance rather than constructing one per scrape, and every read and write of that state is taken under the same
/// lock, because scrapes arrive on whichever thread served them.
/// </para>
/// </remarks>
internal sealed partial class PersonalDataAnalyzerHealthCheck : IHealthCheck
{
    /// <summary>The name the check is registered under.</summary>
    internal const string Name = "personal-data-analyzer";

    private readonly Lock mutex = new();
    private readonly IPersonalDataAnalyzerProbe probe;
    private readonly ISensitiveContentPostures postures;
    private readonly ILogger<PersonalDataAnalyzerHealthCheck> logger;

    private AnalyzerAvailability lastObserved = AnalyzerAvailability.Unobserved;

    /// <summary>Initializes a new personal-data analyzer health check.</summary>
    /// <param name="probe">Asks the configured analyzer whether it can answer for every switched-on category.</param>
    /// <param name="postures">Says whether anybody's mail is scanned for personal data at all.</param>
    /// <param name="logger">Reports the two transitions this check exists to make visible.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public PersonalDataAnalyzerHealthCheck(
        IPersonalDataAnalyzerProbe probe,
        ISensitiveContentPostures postures,
        ILogger<PersonalDataAnalyzerHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(postures);
        ArgumentNullException.ThrowIfNull(logger);

        this.probe = probe;
        this.postures = postures;
        this.logger = logger;
    }

    /// <summary>What this check has established about the analyzer so far.</summary>
    private enum AnalyzerAvailability
    {
        /// <summary>No scrape has reached the analyzer yet, or none has needed to.</summary>
        Unobserved = 0,

        /// <summary>The last scrape got an answer for every switched-on category.</summary>
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
        static provider => provider.GetRequiredService<PersonalDataAnalyzerHealthCheck>(),
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
        Justification = "Whatever stopped the probe, the analyzer did not answer, and an operator needs the reason in the log rather than an unhealthy verdict with nothing behind it.")]
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!this.postures.RunsForAnyOwner(SensitiveContentScannerKind.Pii))
        {
            // Forgotten rather than recorded as available, so an owner who switches the scanner on after an outage
            // began meets a check that reports that outage instead of one holding a verdict about nobody's mail. The
            // transition out of an outage is still written: the probe flips to ready here, and an operator whose log
            // ends at the Error record would otherwise have nothing saying the instance is back in traffic.
            if (this.Observed(AnalyzerAvailability.Unobserved) is AnalyzerAvailability.Unavailable)
            {
                this.LogAnalyzerNoLongerAsked();
            }

            return HealthCheckResult.Healthy(
                "No owner's mail is scanned for personal data, so nothing on this instance asks the analyzer.");
        }

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
            if (this.Observed(AnalyzerAvailability.Unavailable) is not AnalyzerAvailability.Unavailable)
            {
                this.LogAnalyzerUnavailable(failure);
            }

            return HealthCheckResult.Unhealthy(
                "The personal-data analyzer did not answer, so every read, derived write, and egress the scanner guards is being refused.");
        }

        if (this.Observed(AnalyzerAvailability.Available) is not AnalyzerAvailability.Available)
        {
            this.LogAnalyzerAvailable();
        }

        return HealthCheckResult.Healthy("The personal-data analyzer answers for every category the scanner is switched on for.");
    }

    /// <summary>Records what this scrape saw, and reports what the previous one had seen.</summary>
    /// <remarks>
    /// The previous value rather than a changed flag, because the caller decides what a transition is worth saying:
    /// arriving at unavailability and leaving it are two different records, and leaving it because nobody's mail is
    /// scanned any more is a third. The first observation of any kind is a transition, so an instance that comes up
    /// with no analyzer says so once rather than staying silent because nothing changed.
    /// </remarks>
    private AnalyzerAvailability Observed(AnalyzerAvailability availability)
    {
        lock (this.mutex)
        {
            var previous = this.lastObserved;
            this.lastObserved = availability;

            return previous;
        }
    }

    /// <summary>
    /// Reports the analyzer as the reason this instance is unready. The failure is passed whole because it is the only
    /// account of what went wrong; MailFathom's own message names the configuration key rather than the address, for
    /// the reason <see cref="PersonalDataAnalyzerUnavailableException" /> records. The wording is state-neutral because
    /// this record covers an analyzer that has never answered as well as one that has stopped.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The personal-data analyzer is not answering, so this instance reports unready and refuses every read, derived write, and egress the scanner guards. It is not restarted, and it becomes ready by itself once the analyzer answers: correct SensitiveContent:PersonalDataAnalyzer:Endpoint, start the analyzer beside this service, or switch the scanner off.")]
    private partial void LogAnalyzerUnavailable(Exception failure);

    /// <summary>Reports the recovery, which is what tells an operator watching the first record that the outage ended.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The personal-data analyzer answers for every category the scanner is switched on for, so this instance reports ready.")]
    private partial void LogAnalyzerAvailable();

    /// <summary>Reports the other way an outage ends: nobody's mail is scanned for personal data any more, so the analyzer is not asked.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No owner's mail is scanned for personal data any more, so this instance reports ready without asking the analyzer. The outage recorded above no longer holds it out of traffic, and it says nothing about whether the analyzer has recovered.")]
    private partial void LogAnalyzerNoLongerAsked();
}
