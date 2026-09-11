// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Host.Signals;

/// <summary>Reports when this replica lost the signal backplane and when it had it back.</summary>
/// <remarks>
/// <para>
/// The backplane is silent while it works, which is why losing it has to be loud. Nothing else in the deployment says
/// so: a signal that cannot cross is swallowed by the publisher on the raising replica, the client on the other side
/// sees a connection that is still open and hears nothing, and both screens catch up on the client's own refresh — so
/// the whole symptom of an hour without a backplane is mail arriving on a screen several minutes late. An operator
/// learns that from this line and this counter or they learn it from a user.
/// </para>
/// <para>
/// <b>Warning rather than Error, and never readiness.</b> A signal is an optimization over a client that re-reads on
/// its own, so a replica that cannot reach the backplane is still serving every screen correctly; taking it out of
/// rotation would turn a late list into an outage. The transition is what is reported rather than the state, because
/// StackExchange.Redis reconnects on its own and a level that reported the state would either write a line per attempt
/// or write nothing after the first.
/// </para>
/// <para>
/// The one dimension is a closed set of this process's own two words, so nothing here opens a series per endpoint, per
/// connection, or per failure message. What is written carries no part of a signal and no part of the connection
/// string: a failure's own sentence names the endpoint it was dialling, so it is deliberately not one of the things
/// this line says.
/// </para>
/// </remarks>
internal sealed partial class SignalBackplaneTelemetry
{
    /// <summary>Which way across the backplane's availability the transition went.</summary>
    internal const string StateTagName = "mailfathom.client_signals.backplane.state";

    /// <summary>The tag value written when the connection to the endpoint went.</summary>
    internal const string LostState = "lost";

    /// <summary>The tag value written when it came back.</summary>
    internal const string RestoredState = "restored";

    private readonly ILogger<SignalBackplaneTelemetry> logger;
    private readonly Counter<long> transitionCount;

    /// <summary>Initializes the instrument both transitions are counted on.</summary>
    /// <param name="logger">Where a transition is written, at a level an operator watching a deployment sees.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public SignalBackplaneTelemetry(ILogger<SignalBackplaneTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.transitionCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.client_signals.backplane.transitions",
            unit: "{transition}",
            description: "Times this replica lost the signal backplane or had it back, by state.");
    }

    /// <summary>Records that this replica can no longer reach the backplane.</summary>
    /// <remarks>Written whether or not the endpoint ever answered: a replica that never connected and one whose connection dropped are the same condition to the screens that stop being told things.</remarks>
    internal void RecordLost()
    {
        this.transitionCount.Add(1, new TagList { { StateTagName, LostState } });
        this.LogBackplaneLost();
    }

    /// <summary>Records that this replica has the backplane back.</summary>
    /// <remarks>What the gap is measured against. Nothing is replayed across it — a statement raised while the connection was down reached nobody and is not buffered anywhere — so the client's own refresh is what closes it.</remarks>
    internal void RecordRestored()
    {
        this.transitionCount.Add(1, new TagList { { StateTagName, RestoredState } });
        this.LogBackplaneRestored();
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The client signal backplane is not reachable from this replica. Live updates still reach a client connected to this replica; one connected to another replica catches up on its own refresh until the connection is back.")]
    private partial void LogBackplaneLost();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The client signal backplane is reachable from this replica again. Signals raised while it was not reached nobody and are not replayed; a client catches up on its own refresh.")]
    private partial void LogBackplaneRestored();
}
