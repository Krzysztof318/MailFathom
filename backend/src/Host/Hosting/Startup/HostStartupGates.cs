// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Tracks which of the host's startup gates have completed, which is what the startup probe reports.</summary>
/// <remarks>
/// <para>
/// The expected gates are stated once, by the composition root that registers the services performing them, so a gate
/// that is added and never reported keeps the probe unhealthy rather than being silently absent from the answer.
/// </para>
/// <para>
/// A gate that fails does not report itself. It throws instead and takes the host down, so the states this distinguishes
/// are "still coming up" and "finished", which is exactly what a startup probe asks. Completion is latching: a gate runs
/// once during startup and nothing sets it back, so an orchestrator that has seen a healthy startup probe keeps seeing
/// one and hands the process over to the liveness and readiness probes. Nothing here re-runs a gate's work either — the
/// startup probe reads a flag rather than reaching a dependency, which is what keeps polling it free.
/// </para>
/// <para>
/// The instance is a singleton written from the startup path and read from probe requests arriving on another thread,
/// so every read and write of the pending set is taken under the same lock.
/// </para>
/// </remarks>
internal sealed class HostStartupGates
{
    private readonly Lock mutex = new();
    private readonly HashSet<HostStartupGate> pendingGates;

    /// <summary>Initializes the tracker over the gates this host runs.</summary>
    /// <param name="expectedGates">Every gate that must complete before the host has finished coming up.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expectedGates" /> is <see langword="null" />.</exception>
    /// <remarks>An empty set reports completion immediately, which is the honest answer for a host that runs no gate rather than a reason to refuse the configuration.</remarks>
    public HostStartupGates(params IReadOnlyList<HostStartupGate> expectedGates)
    {
        ArgumentNullException.ThrowIfNull(expectedGates);

        this.pendingGates = [.. expectedGates];
    }

    /// <summary>Gets whether every expected gate has completed.</summary>
    internal bool Completed
    {
        get
        {
            lock (this.mutex)
            {
                return this.pendingGates.Count == 0;
            }
        }
    }

    /// <summary>Records that a gate has completed.</summary>
    /// <param name="gate">The gate that finished.</param>
    /// <remarks>Reporting a gate this host does not expect changes nothing, so a service that is registered conditionally can report unconditionally.</remarks>
    internal void MarkCompleted(HostStartupGate gate)
    {
        lock (this.mutex)
        {
            this.pendingGates.Remove(gate);
        }
    }
}
