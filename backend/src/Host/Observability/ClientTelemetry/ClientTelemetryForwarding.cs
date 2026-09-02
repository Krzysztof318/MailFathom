// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>What became of one batch the proxy sent on, and what the client is owed as a result.</summary>
/// <param name="Failure">The condition that stopped the batch, or <see cref="ClientTelemetryFailure.None" /> where it arrived.</param>
/// <param name="Body">What the destination answered, relayed to the client so a partial success is not hidden behind a success.</param>
/// <param name="RetryAfter">How long the destination asked to be left alone for, where it said.</param>
/// <remarks>
/// One type covers arrival, refusal, and failure because the caller answers all three from the same place and the
/// difference between them is exactly one value. Splitting it would put the question of which case a result is into the
/// type system and the question of what to answer for it somewhere else.
/// </remarks>
internal readonly record struct ClientTelemetryForwarding(
    ClientTelemetryFailure Failure,
    byte[] Body,
    TimeSpan? RetryAfter)
{
    /// <summary>Gets whether the batch reached the destination.</summary>
    internal bool Arrived => this.Failure == ClientTelemetryFailure.None;

    /// <summary>Reports a batch the destination accepted, carrying whatever it answered.</summary>
    /// <param name="body">The destination's own answer, which is where a partial success lives.</param>
    /// <returns>The forwarding.</returns>
    internal static ClientTelemetryForwarding Forwarded(byte[] body) =>
        new(ClientTelemetryFailure.None, body, RetryAfter: null);

    /// <summary>Reports a batch the destination will never accept.</summary>
    /// <returns>The forwarding.</returns>
    /// <remarks>
    /// What the destination said about it is deliberately not carried. A refusal from a collector is about this
    /// deployment's own export — a credential it would not take, a version it does not speak — and relaying that
    /// document to a browser would publish the deployment's infrastructure to whoever is signed in. A partial success
    /// is the one answer a client is owed verbatim, and that arrives as an arrival rather than as this.
    /// </remarks>
    internal static ClientTelemetryForwarding Refused() =>
        new(ClientTelemetryFailure.Refused, [], RetryAfter: null);

    /// <summary>Reports a destination asking to be sent less.</summary>
    /// <param name="retryAfter">How long it asked for, where it said.</param>
    /// <returns>The forwarding.</returns>
    internal static ClientTelemetryForwarding Throttled(TimeSpan? retryAfter) =>
        new(ClientTelemetryFailure.Throttled, [], retryAfter);

    /// <summary>Reports a batch that did not arrive and may arrive later.</summary>
    /// <param name="failure">Which condition stopped it.</param>
    /// <returns>The forwarding.</returns>
    internal static ClientTelemetryForwarding Failed(ClientTelemetryFailure failure) => new(failure, [], RetryAfter: null);
}

/// <summary>Why a batch did not reach the destination, at the level a deployment acts on.</summary>
/// <remarks>
/// These are conditions rather than incidents, which is what makes the proxy quiet: a collector that has been down for
/// an hour is one of these repeating, and the log line the deployment reads is written per condition rather than per
/// batch.
/// </remarks>
internal enum ClientTelemetryFailure
{
    /// <summary>The batch arrived.</summary>
    None = 0,

    /// <summary>The destination will never accept the batch, and said so.</summary>
    Refused = 1,

    /// <summary>The destination asked to be sent less.</summary>
    Throttled = 2,

    /// <summary>The destination answered that it could not take the batch now.</summary>
    Unavailable = 3,

    /// <summary>The destination did not answer inside the exporter's own configured timeout.</summary>
    TimedOut = 4,

    /// <summary>Nothing answered at the destination's address at all.</summary>
    Unreachable = 5,

    /// <summary>The client that sent the batch disconnected before the destination answered.</summary>
    Cancelled = 6,
}
