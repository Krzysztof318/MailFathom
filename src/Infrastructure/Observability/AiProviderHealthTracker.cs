// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MailFathom.Application.AiProviders;
using MailFathom.Common.Observability;

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
/// Nothing here is mail or derived from it. The only tag is the role, one of two constants, and the only values are a
/// state and the moment it was observed — which is also a cardinality rule, since anything per endpoint or per request
/// would open a time series that grows with the deployment.
/// </para>
/// </remarks>
public sealed class AiProviderHealthTracker : IAiProviderHealthRecorder, IAiProviderHealthReader
{
    private const string RoleTagName = "mailfathom.ai.provider.role";

    private readonly ConcurrentDictionary<AiProviderRole, AiProviderHealth> states = new();
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a tracker that has observed nothing yet, and the instrument it publishes through.</summary>
    /// <param name="timeProvider">Stamps each observation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public AiProviderHealthTracker(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;

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

    private void Record(AiProviderRole role, AiProviderHealthState state) =>
        this.states[role] = new AiProviderHealth(role, state, this.timeProvider.GetUtcNow());

    private static string RoleTagOf(AiProviderRole role) => role switch
    {
        AiProviderRole.Embedding => "embedding",
        AiProviderRole.Chat => "chat",
        _ => "unknown",
    };
}
