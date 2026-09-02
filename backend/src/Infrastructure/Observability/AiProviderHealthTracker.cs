// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MailFathom.Application.AiProviders;
using MailFathom.Common.Observability;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Holds what the last call to each AI provider established, and publishes both states as one instrument.</summary>
/// <remarks>
/// <para>
/// One tracker for both roles, with the role as a key rather than as a second class. That is what makes the two states
/// independently observable without being independently implemented: a reader asks for one role and receives that
/// role's answer, and the gauge below carries one measurement per role so a dashboard can show either without the other
/// hiding it.
/// </para>
/// <para>
/// The gauge reports a number rather than a name, because an instrument's value has to be one. It publishes the state's
/// own enumeration value, whose members are allocated once and never reordered, so a recording rule written against
/// <c>2</c> keeps meaning "unavailable" for as long as the series exists.
/// </para>
/// <para>
/// A transition is logged as well as measured, because a metric answers what the state is now and an operator reading
/// afterwards needs to know when it changed and to what. Only a change is logged: every provider call records, and a
/// line per call would put the log's volume on the mailbox's size rather than on anything an operator would act on. The
/// one change that is not logged is a first call that succeeded, which restores nothing.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. The only tag is the role, one of two constants, and the only values are a
/// state and the moment it was observed — which is also a cardinality rule, since anything per endpoint or per request
/// would open a time series that grows with the deployment. The log lines carry the same two things and no credential,
/// endpoint address, request, or provider response text.
/// </para>
/// </remarks>
public sealed partial class AiProviderHealthTracker : IAiProviderHealthRecorder, IAiProviderHealthReader
{
    private const string RoleTagName = "mailfathom.ai.provider.role";

    private readonly ConcurrentDictionary<AiProviderRole, AiProviderHealth> states = new();
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AiProviderHealthTracker> logger;

    /// <summary>Initializes a tracker that has observed nothing yet, and the instrument it publishes through.</summary>
    /// <param name="timeProvider">Stamps each observation.</param>
    /// <param name="logger">Records a transition between two states, in the role and the classification alone.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> or <paramref name="logger" /> is <see langword="null" />.</exception>
    public AiProviderHealthTracker(TimeProvider timeProvider, ILogger<AiProviderHealthTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.timeProvider = timeProvider;
        this.logger = logger;

        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.ai.provider.health",
            this.ObserveStates,
            unit: "{state}",
            description: "What the last call to each AI provider established about it, as the state's own value.");
    }

    /// <inheritdoc />
    public void RecordServed(AiProviderRole role) => this.Record(role, AiProviderHealthState.Serving);

    /// <inheritdoc />
    public void RecordUnavailable(AiProviderRole role) => this.Record(role, AiProviderHealthState.Unavailable);

    /// <inheritdoc />
    public void RecordMisconfigured(AiProviderRole role) => this.Record(role, AiProviderHealthState.Misconfigured);

    /// <inheritdoc />
    /// <remarks>A role nothing has recorded reads as unobserved rather than as an absence, so no caller has to decide what a missing entry means.</remarks>
    public AiProviderHealth Read(AiProviderRole role) => this.states.TryGetValue(role, out var state)
        ? state
        : new AiProviderHealth(role, AiProviderHealthState.Unobserved, ObservedAt: null);

    /// <summary>Reports every role a call has been made to.</summary>
    /// <remarks>
    /// A role nothing has called publishes no measurement, deliberately. A series of zeroes for a provider this
    /// deployment does not use would look like a provider being watched, and an operator would have to know which of the
    /// two flat lines meant "nothing configured".
    /// </remarks>
    private IEnumerable<Measurement<int>> ObserveStates() =>
    [
        .. this.states.Values.Select(static health => new Measurement<int>(
            (int)health.State,
            new KeyValuePair<string, object?>(RoleTagName, RoleTagOf(health.Role)))),
    ];

    /// <summary>Stores what one call established, and says so when it is not what the call before it established.</summary>
    /// <remarks>
    /// Two calls that land together can both read the same previous state and both report the transition, which is a
    /// duplicate line rather than a wrong one: the stored state is whichever wrote last either way, and a lock around a
    /// dictionary write on the path of every provider call would cost more than the duplicate does.
    /// </remarks>
    private void Record(AiProviderRole role, AiProviderHealthState state)
    {
        var previousState = this.Read(role).State;

        this.states[role] = new AiProviderHealth(role, state, this.timeProvider.GetUtcNow());

        if (previousState == state)
        {
            return;
        }

        if (state is not AiProviderHealthState.Serving)
        {
            this.LogProviderStoppedAnswering(role, previousState, state);

            return;
        }

        // The first call an instance makes is not a recovery, however it ends. A success there restores nothing —
        // nothing was degraded — and a line claiming otherwise on every start is one an operator learns to skip before
        // the one that matters arrives. A first *failure* is reported, because that one is news.
        if (previousState is not AiProviderHealthState.Unobserved)
        {
            this.LogProviderAnsweringAgain(role, previousState);
        }
    }

    private static string RoleTagOf(AiProviderRole role) => role switch
    {
        AiProviderRole.Embedding => "embedding",
        AiProviderRole.Chat => "chat",
        _ => "unknown",
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The {AiProviderRole} provider moved from {PreviousState} to {State}. Whatever depends on it is degraded until a later call succeeds; no restart is needed to pick that up.")]
    private partial void LogProviderStoppedAnswering(
        AiProviderRole aiProviderRole,
        AiProviderHealthState previousState,
        AiProviderHealthState state);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The {AiProviderRole} provider answered again after {PreviousState}; whatever it serves is no longer degraded.")]
    private partial void LogProviderAnsweringAgain(AiProviderRole aiProviderRole, AiProviderHealthState previousState);
}
