// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns the declared generation parameters into the options one request carries.</summary>
/// <remarks>
/// <para>
/// One mapping rather than one per caller, because the single-request adapter and the answering run send the same
/// parameters to the same endpoint and differ only in what each adds on top. Two copies would let a parameter reach one
/// path and not the other, and the deployment would see a setting that worked for a question and was ignored for a
/// judgement.
/// </para>
/// <para>
/// It is also what keeps every parameter optional in the way the declaration means it: an unwritten value produces no
/// member on the options object, so the request carries nothing rather than a default this side invented. Several
/// current models reject a sampling parameter or a reasoning effort outright, so sending one nobody asked for turns
/// every call a deployment makes into a rejected request.
/// </para>
/// </remarks>
internal static class ChatGenerationParameterMapping
{
    /// <summary>Builds the options one request carries from what the deployment declared.</summary>
    /// <param name="plan">The validated declaration.</param>
    /// <returns>The options, carrying only the parameters the declaration wrote.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> is <see langword="null" />.</exception>
    public static ChatOptions ToChatOptions(ChatGenerationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new ChatOptions
        {
            MaxOutputTokens = plan.MaximumOutputTokens,
            Temperature = plan.Temperature,
            TopP = plan.TopP,
            Reasoning = ToReasoningOptions(plan.ReasoningEffort),
        };
    }

    /// <summary>Reads the declared effort into the reasoning block a request carries, or into nothing at all.</summary>
    /// <remarks>
    /// The null branch is the whole point of the property being nullable: a model that does not reason refuses the
    /// parameter, so an absent declaration has to leave the block off the request rather than send an effort of none.
    /// </remarks>
    private static ReasoningOptions? ToReasoningOptions(ChatReasoningEffort? effort) =>
        effort is { } declared
            ? new ReasoningOptions { Effort = ToReasoningEffort(declared) }
            : null;

    /// <summary>Reads a declared effort into the one a request carries.</summary>
    /// <remarks>
    /// The refusing arm is unreachable through a plan, which refuses an undeclared value at startup, and it is written
    /// rather than collapsed into one of the named results because the alternative is a request that silently states an
    /// effort nobody wrote. A provider answers such a request rather than refusing it, so nothing downstream could tell.
    /// </remarks>
    private static ReasoningEffort ToReasoningEffort(ChatReasoningEffort effort) => effort switch
    {
        ChatReasoningEffort.None => ReasoningEffort.None,
        ChatReasoningEffort.Low => ReasoningEffort.Low,
        ChatReasoningEffort.Medium => ReasoningEffort.Medium,
        ChatReasoningEffort.High => ReasoningEffort.High,
        ChatReasoningEffort.ExtraHigh => ReasoningEffort.ExtraHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "The declared reasoning effort names no value a request can carry."),
    };
}
