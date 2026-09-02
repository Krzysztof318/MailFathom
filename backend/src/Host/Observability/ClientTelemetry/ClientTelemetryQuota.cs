// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Threading.RateLimiting;

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>Bounds how often one signed-in person's client may export through this deployment.</summary>
/// <remarks>
/// <para>
/// A bucket of its own rather than the surface's, and the reason is where each of the two runs. The client endpoint's
/// limiter runs ahead of the authorization middleware, so every caller shares one partition until a credential has been
/// judged — which is the stronger bound for unbounded key guessing and exactly the wrong shape here, because the
/// callers are all authenticated and one of them exporting continuously would spend the capacity a person reading their
/// mail needs. This runs behind authentication, where there is an owner to count against.
/// </para>
/// <para>
/// The numbers are constants rather than settings. What an operator configures is whether telemetry is forwarded at
/// all, which is the destination variable; how fast one browser may push is a property of what a client exports rather
/// than of a deployment, and a knob here would be one nobody could set correctly without reading this file anyway. The
/// capacity is a minute's worth of exports at the rate an OpenTelemetry pipeline's default interval produces them
/// across three signals, with room for a client that has just come back from being offline.
/// </para>
/// <para>
/// Refusing costs a batch rather than a client. A refused export is answered with the status the OTLP specification
/// names for throttling, so the client holds what it has not exported and sends it with the next one.
/// </para>
/// </remarks>
internal sealed class ClientTelemetryQuota : IDisposable
{
    /// <summary>The most exports one owner may have outstanding capacity for at any instant.</summary>
    internal const int BurstCapacity = 120;

    /// <summary>How much capacity one owner regains each period.</summary>
    internal const int ExportsPerPeriod = 120;

    /// <summary>How long a refused client is asked to hold for, which is one replenishment away from having capacity again.</summary>
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan ReplenishmentPeriod = RetryAfter;

    private readonly PartitionedRateLimiter<string> exports = PartitionedRateLimiter.Create<string, string>(
        owner => RateLimitPartition.GetTokenBucketLimiter(
            owner,
            _ => new TokenBucketRateLimiterOptions
            {
                AutoReplenishment = true,
                TokenLimit = BurstCapacity,
                TokensPerPeriod = ExportsPerPeriod,
                ReplenishmentPeriod = ReplenishmentPeriod,

                // Nothing waits. A client holding what it could not export is the contract this endpoint already
                // works to, so queuing a batch here would hold a request open to deliver what the client is
                // perfectly able to send again.
                QueueLimit = 0,
            }),
        StringComparer.Ordinal);

    /// <summary>Spends one export's capacity for one owner, or reports that there is none left.</summary>
    /// <param name="owner">The owner the export was attributed to.</param>
    /// <returns><see langword="true" /> when the export may proceed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    /// <remarks>The lease is released immediately, which returns nothing to a token bucket: what it holds is permission that has already been spent.</remarks>
    internal bool TryAdmit(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        using var lease = this.exports.AttemptAcquire(owner);

        return lease.IsAcquired;
    }

    /// <inheritdoc />
    public void Dispose() => this.exports.Dispose();
}
