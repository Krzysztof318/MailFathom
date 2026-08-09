// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.AI.Chat;

/// <summary>The validated declaration the chat adapter runs on: which endpoint answers, with which parameters, and what one call may spend.</summary>
/// <remarks>
/// <para>
/// Built once, at startup, from configuration that has already been proved usable. The adapter therefore never
/// revalidates and holds no defaulting logic of its own.
/// </para>
/// <para>
/// One endpoint rather than a chain, which is the deliberate difference from the embedding declaration. A fallback
/// embedding endpoint is another route to one vector space — startup proves every endpoint of a chain declares the same
/// geometry, so it cannot change what a vector means. Nothing proves that of two chat models: falling through would
/// silently answer a person in a different model's voice, with different capabilities and different refusals, and
/// nothing above this boundary could tell it had happened. An operator who wants failover puts a gateway in front of one
/// declared endpoint, where the substitution is theirs and is visible to them.
/// </para>
/// <para>
/// Deliberately holds no credential and no secret reference. What proves the deployment's identity to the endpoint is
/// resolved per request, so a rotated key needs no restart and this value is safe to hold for the lifetime of the
/// process.
/// </para>
/// </remarks>
public sealed partial class ChatGenerationPlan
{
    /// <summary>The longest a reasoning effort may be, past which it is plainly not a level.</summary>
    /// <remarks>Generous against every level any provider publishes, because the bound exists to catch a value that is not a level at all rather than to predict the next one.</remarks>
    private const int MaximumReasoningEffortLength = 32;

    private ChatGenerationPlan(
        ChatEndpoint endpoint,
        int maximumOutputTokens,
        float? temperature,
        float? topP,
        string? reasoningEffort,
        int maximumMessagesPerRequest,
        int maximumRequestCharacters,
        TimeSpan requestTimeout)
    {
        this.Endpoint = endpoint;
        this.MaximumOutputTokens = maximumOutputTokens;
        this.Temperature = temperature;
        this.TopP = topP;
        this.ReasoningEffort = reasoningEffort;
        this.MaximumMessagesPerRequest = maximumMessagesPerRequest;
        this.MaximumRequestCharacters = maximumRequestCharacters;
        this.RequestTimeout = requestTimeout;
    }

    /// <summary>Gets the endpoint every request is sent to.</summary>
    public ChatEndpoint Endpoint { get; }

    /// <summary>Gets the greatest number of tokens one answer may occupy.</summary>
    /// <remarks>
    /// The one generation parameter with no useful provider default: left unset, a model is free to generate until it
    /// stops, and a deployment cannot bound what a single call costs. Reaching it is not a failure — the answer arrives
    /// with <c>OutputLimitReached</c> and the text before the cut is real.
    /// </remarks>
    public int MaximumOutputTokens { get; }

    /// <summary>Gets the sampling temperature, or <see langword="null" /> to leave the model's own default in place.</summary>
    /// <remarks>Nullable rather than defaulted, because several current models reject the parameter outright, and sending a value the model refuses turns every call into a rejected request.</remarks>
    public float? Temperature { get; }

    /// <summary>Gets the nucleus-sampling threshold, or <see langword="null" /> to leave the model's own default in place.</summary>
    /// <remarks>Nullable for the reason <see cref="Temperature" /> is, and declared beside it rather than instead of it because a provider that accepts both documents setting only one.</remarks>
    public float? TopP { get; }

    /// <summary>Gets the reasoning effort every call states, or <see langword="null" /> to send no reasoning parameter at all.</summary>
    /// <remarks>
    /// <para>
    /// Nullable for the reason the two sampling parameters are, and with one more consequence of its own: a model that
    /// does not reason refuses the parameter outright, so a literal default here would turn every call such a deployment
    /// makes into a rejected request. Writing <c>none</c> is therefore not the same as writing nothing — it states an
    /// effort of none and sends it, which is what a provider refusing an unstated effort beside function tools asks for.
    /// </para>
    /// <para>
    /// The value is the provider's own, carried through as written rather than mapped from a set declared here. Which
    /// efforts exist is a property of the model: <c>xhigh</c> arrived after the levels beneath it and a model released
    /// after this version may add another, so a closed set would make a rebuild the price of using one — which is the
    /// same reason the routed model name is a string. What this side owns is the shape of the value, not its meaning,
    /// and a value the model does not know is a request the provider refuses rather than one this can rule out first.
    /// </para>
    /// </remarks>
    public string? ReasoningEffort { get; }

    /// <summary>Gets the greatest number of turns one request carries.</summary>
    public int MaximumMessagesPerRequest { get; }

    /// <summary>Gets the greatest number of characters the turns of one request may add up to.</summary>
    /// <remarks>
    /// A bound on what leaves the deployment, expressed in the unit this side can measure. Tokens are what the provider
    /// bills and refuses by, but counting them means carrying the model's own tokenizer, so the ceiling is stated in
    /// characters and set below what the context window allows. It exists so an oversized conversation is refused here
    /// rather than after being built, sent, and billed for.
    /// </remarks>
    public int MaximumRequestCharacters { get; }

    /// <summary>Gets the time one request may take before it is abandoned.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Builds a plan, refusing a declaration no request could be made from.</summary>
    /// <param name="endpoint">The endpoint every request is sent to.</param>
    /// <param name="maximumOutputTokens">The greatest number of tokens one answer may occupy.</param>
    /// <param name="temperature">The sampling temperature, or <see langword="null" /> for the model's own default.</param>
    /// <param name="topP">The nucleus-sampling threshold, or <see langword="null" /> for the model's own default.</param>
    /// <param name="reasoningEffort">The reasoning effort every call states, or <see langword="null" /> to send none.</param>
    /// <param name="maximumMessagesPerRequest">The greatest number of turns one request carries.</param>
    /// <param name="maximumRequestCharacters">The greatest number of characters those turns may add up to.</param>
    /// <param name="requestTimeout">The time one request may take.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the endpoint declares a blank alias or a blank routed model name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is not positive, a sampling parameter is outside the range every provider accepts, or the endpoint's API or the reasoning effort names no declared value.</exception>
    public static ChatGenerationPlan Create(
        ChatEndpoint endpoint,
        int maximumOutputTokens,
        float? temperature,
        float? topP,
        string? reasoningEffort,
        int maximumMessagesPerRequest,
        int maximumRequestCharacters,
        TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Alias, nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.RoutedModelName, nameof(endpoint));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMessagesPerRequest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestTimeout, TimeSpan.Zero);

        RequireSamplingParameterInRange(temperature, nameof(temperature), greatest: 2f);
        RequireSamplingParameterInRange(topP, nameof(topP), greatest: 1f);

        if (!Enum.IsDefined(endpoint.Api))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endpoint),
                endpoint.Api,
                "The endpoint names no API this adapter can reach.");
        }

        if (reasoningEffort is not null && !IsUsableReasoningEffort(reasoningEffort))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasoningEffort),
                reasoningEffort,
                "The reasoning effort is not a single word a provider could read as a level.");
        }

        return new ChatGenerationPlan(
            endpoint,
            maximumOutputTokens,
            temperature,
            topP,
            reasoningEffort,
            maximumMessagesPerRequest,
            maximumRequestCharacters,
            requestTimeout);
    }

    /// <summary>Refuses a sampling parameter outside the range providers accept.</summary>
    /// <remarks>
    /// Checked here rather than left to the provider, because a value outside the range is rejected on every call this
    /// deployment ever makes, and learning that from a paid request is learning it late.
    /// </remarks>
    private static void RequireSamplingParameterInRange(float? value, string parameterName, float greatest)
    {
        if (value is not { } declared)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(declared, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(declared, greatest, parameterName);
    }

    /// <summary>Reports whether a reasoning effort is shaped like a level a provider could read.</summary>
    /// <param name="effort">The declared effort.</param>
    /// <returns><see langword="true" /> when the value could be a level, <see langword="false" /> when no provider could read it as one.</returns>
    /// <remarks>
    /// A shape check and deliberately not a vocabulary one. Which levels exist belongs to the model, so refusing a value
    /// against a list held here is exactly the rebuild this parameter is a string to avoid — a model that adds one must
    /// be usable without a release. What is refused is a value no provider could read as a level whatever it supports:
    /// blank, padded, or long enough that it is plainly not a level, none of which a request should be spent learning.
    /// <para>
    /// Published so the configuration layer refuses the same values this does. The rule is one decision and a second
    /// copy of it would drift, leaving a value startup accepted and the plan then threw on.
    /// </para>
    /// </remarks>
    public static bool IsUsableReasoningEffort(string effort) =>
        effort.Length is > 0 and <= MaximumReasoningEffortLength && ReasoningEffortShape().IsMatch(effort);

    /// <remarks>
    /// Anchored with <c>\A</c> and <c>\z</c> rather than <c>^</c> and <c>$</c>, because <c>$</c> also matches before a
    /// trailing newline: a value provisioned from a file ends in one, and <c>high\n</c> would otherwise pass the shape
    /// check and go out on the wire as a level no provider reads. Non-backtracking because the value is an operator's
    /// and the pattern's shape is the one that backtracks badly, though the length bound already caps it.
    /// </remarks>
    [GeneratedRegex(@"\A[a-zA-Z0-9]+([-_][a-zA-Z0-9]+)*\z", RegexOptions.NonBacktracking)]
    private static partial Regex ReasoningEffortShape();
}
