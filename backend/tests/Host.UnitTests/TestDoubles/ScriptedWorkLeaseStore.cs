// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Threading.Channels;
using MailFathom.Application.Coordination;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Stands in for the lease table: grants every scope nothing holds, and lets a test play the other replica.</summary>
/// <remarks>
/// A lease granted here never expires, because nothing under test reads an expiry: a holder stops on the renewal it
/// failed rather than on the clock, and a test that wants a scope to have moved on says so through
/// <see cref="HeldElsewhere" />.
/// </remarks>
internal sealed class ScriptedWorkLeaseStore : IWorkLeaseStore
{
    /// <summary>The replica every lease this store grants is stamped with, since one store stands in for one process.</summary>
    internal static readonly ReplicaIdentity Replica = ReplicaIdentity.Create("scripted-replica:1");

    private readonly ConcurrentDictionary<WorkScope, WorkLeaseHolder> holders = new();
    private readonly ConcurrentQueue<WorkScope> releases = new();
    private readonly Channel<WorkScope> claims = Channel.CreateUnbounded<WorkScope>();

    /// <summary>Gets or sets whether another replica holds every scope, so a claim is refused and a held lease is not renewed.</summary>
    internal bool HeldElsewhere { get; set; }

    /// <summary>Gets the scopes given back, in the order they were.</summary>
    internal IReadOnlyCollection<WorkScope> Releases => this.releases;

    /// <summary>Gets a snapshot of the scopes held through this store right now.</summary>
    internal IReadOnlyCollection<WorkScope> HeldScopes => [.. this.holders.Keys];

    /// <summary>Waits for the next claim to have been answered, whichever way it was.</summary>
    internal Task WaitForClaimAsync(CancellationToken cancellationToken) => this.claims.Reader.ReadAsync(cancellationToken).AsTask();

    public Task<WorkLease?> ClaimAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var granted = !this.HeldElsewhere && this.holders.TryAdd(scope, holder);

        this.claims.Writer.TryWrite(scope);

        return Task.FromResult(granted ? new WorkLease(scope, holder, Replica, DateTimeOffset.MaxValue) : null);
    }

    public Task<WorkLease?> RenewAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var renewed = !this.HeldElsewhere && this.holders.TryGetValue(scope, out var recorded) && recorded == holder;

        return Task.FromResult(renewed ? new WorkLease(scope, holder, Replica, DateTimeOffset.MaxValue) : null);
    }

    public Task<bool> ReleaseAsync(WorkScope scope, WorkLeaseHolder holder, CancellationToken cancellationToken)
    {
        var released = !this.HeldElsewhere && this.holders.TryRemove(KeyValuePair.Create(scope, holder));

        if (released)
        {
            this.releases.Enqueue(scope);
        }

        return Task.FromResult(released);
    }

    public Task<IReadOnlyList<WorkLease>> ReadHeldAsync(
        IReadOnlyCollection<WorkScope> scopes,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkLease> held =
        [
            .. scopes
                .Where(this.holders.ContainsKey)
                .Select(scope => new WorkLease(scope, this.holders[scope], Replica, DateTimeOffset.MaxValue)),
        ];

        return Task.FromResult(held);
    }
}
