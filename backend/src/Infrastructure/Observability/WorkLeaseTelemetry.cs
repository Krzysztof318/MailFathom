// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Coordination;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Makes exclusive hold of work legible from outside the process: what this replica holds, and how often a scope changed hands.</summary>
/// <remarks>
/// <para>
/// Three instruments, and each answers a question the others cannot. The gauge says which scopes <em>this</em> replica
/// is holding right now, which is how "who holds what" is read across a deployment — the replica is named by the
/// resource attributes every measurement it publishes already carries, so one series per scope per replica answers it
/// without the holder identity ever becoming a dimension. The claims say how often a scope changed hands, which is the
/// signal that work is moving between replicas rather than sitting where it started; a granted claim is by
/// construction a change of holder, because a live lease is refused whoever asks for it. And the renewals say when a
/// holder lost a scope it thought it had, which is the one thing that stops work: a refused renewal is a replica
/// cancelling a run, and it would otherwise be invisible from outside.
/// </para>
/// <para>
/// A release is deliberately not a fourth instrument. What it does is drop the scope out of the gauge, which is the
/// same reading a refused renewal produces, and both are what an operator watching a rolling upgrade wants to see.
/// </para>
/// <para>
/// <strong>Nothing published here is mail or derived from it.</strong> The one dimension is the scope, which is
/// composed of MailFathom's own names — an account alias, a user identity, the name of a walk — and is bounded by how
/// much singleton work the deployment configures. The holder is off every measurement, because it is a fresh identity
/// per hold and would make a new series of every takeover; an operator who needs it reads the lease table, which is
/// where it is kept.
/// </para>
/// </remarks>
public sealed class WorkLeaseTelemetry
{
    internal const string ScopeTagName = "mailfathom.work.scope";
    internal const string OutcomeTagName = "mailfathom.work.lease.outcome";

    private readonly ConcurrentDictionary<string, byte> heldScopes = new(StringComparer.Ordinal);
    private readonly Counter<long> claimCount;
    private readonly Counter<long> renewalCount;

    /// <summary>Initializes the instruments every claim, renewal, and release reports through.</summary>
    public WorkLeaseTelemetry()
    {
        this.claimCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.work_leases.claims",
            unit: "{claim}",
            description: "Attempts to take exclusive hold of a unit of work, by scope and whether the hold was granted.");
        this.renewalCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.work_leases.renewals",
            unit: "{renewal}",
            description: "Attempts to extend a held lease, by scope and whether the holder still held it.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.work_leases.held",
            this.ObserveHeldScopes,
            unit: "{scope}",
            description: "Units of work this replica is currently holding a lease on, one series per scope.");
    }

    /// <summary>Records an attempt to take a scope, and starts publishing the scope when the attempt took it.</summary>
    /// <param name="scope">The unit of work the claim asked for.</param>
    /// <param name="granted">Whether the claim took the scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public void RecordClaim(WorkScope scope, bool granted)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.claimCount.Add(1, new TagList { { ScopeTagName, scope.Value }, { OutcomeTagName, OutcomeTagOf(granted) } });

        if (granted)
        {
            this.heldScopes[scope.Value] = 0;
        }
    }

    /// <summary>Records an attempt to extend a lease, and stops publishing the scope when the holder had lost it.</summary>
    /// <param name="scope">The unit of work the renewal asked for.</param>
    /// <param name="granted">Whether the holder still held the scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A refused renewal takes the scope out of the gauge here rather than waiting for the holder to act on it, so the
    /// two replicas involved never both publish it: the one that took the scope started publishing it when its claim
    /// was granted, and this is the moment the one that lost it stops.
    /// </remarks>
    public void RecordRenewal(WorkScope scope, bool granted)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.renewalCount.Add(
            1,
            new TagList { { ScopeTagName, scope.Value }, { OutcomeTagName, OutcomeTagOf(granted) } });

        if (!granted)
        {
            this.heldScopes.TryRemove(scope.Value, out _);
        }
    }

    /// <summary>Stops publishing a scope this replica has given back.</summary>
    /// <param name="scope">The unit of work that was released.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Called whether the release wrote anything or not. A release that found the scope held by somebody else is
    /// exactly the case where this replica must stop claiming to hold it, so the reading is the same either way.
    /// </remarks>
    public void RecordRelease(WorkScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.heldScopes.TryRemove(scope.Value, out _);
    }

    private static string OutcomeTagOf(bool granted) => granted ? "granted" : "refused";

    /// <summary>Reads the scopes this replica holds, materialized before the meter sees them.</summary>
    /// <remarks>
    /// A gauge callback is invoked on the collector's schedule, so a deferred query would be enumerated against
    /// whatever the dictionary held then rather than against what this call read.
    /// </remarks>
    private IEnumerable<Measurement<long>> ObserveHeldScopes() =>
    [
        .. this.heldScopes.Select(held => new Measurement<long>(1, new TagList { { ScopeTagName, held.Key } })),
    ];
}
