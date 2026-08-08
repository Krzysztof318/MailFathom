// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Embeddings;

/// <summary>What a deployment says activating its declaration would do, and what it would cost.</summary>
/// <remarks>
/// Read before the activation is asked for, so the operator agrees to a number rather than to a word. The deployment
/// weighs the same figures again when the activation arrives, which is what keeps a confirmation from being a promise
/// about state that has since moved.
/// </remarks>
/// <param name="Declared">The vector space that would be activated.</param>
/// <param name="Forecast">What activating it would do.</param>
/// <param name="Estimate">What that would cost.</param>
/// <param name="Spend">Where the deployment's budget period stands.</param>
/// <param name="ExceedsSpendCeiling">Whether the deployment's ceiling refuses this activation outright.</param>
internal sealed record EmbeddingActivationAssessment(
    [property: JsonPropertyName("declared")] EmbeddingGeometry? Declared,
    [property: JsonPropertyName("forecast")] string? Forecast,
    [property: JsonPropertyName("estimate")] EmbeddingWorkload? Estimate,
    [property: JsonPropertyName("spend")] EmbeddingSpend? Spend,
    [property: JsonPropertyName("exceedsSpendCeiling")] bool ExceedsSpendCeiling)
{
    /// <summary>The forecast naming the one activation that spends.</summary>
    /// <remarks>
    /// Matched as a name rather than parsed into an enumeration of this command's own, because the deployment owns the
    /// set. A build reporting a forecast this command has never heard of is therefore possible, and is treated as
    /// spending — asking a question that was not needed costs a keystroke, and skipping one that was costs a mailbox.
    /// </remarks>
    internal const string WouldStartReindex = nameof(WouldStartReindex);

    /// <summary>The forecast saying the declaration is already the generation searches are answered from.</summary>
    internal const string AlreadyServing = nameof(AlreadyServing);

    /// <summary>The forecast saying the declaration is already the generation a reindex is filling.</summary>
    internal const string WouldResumeReindex = nameof(WouldResumeReindex);

    /// <summary>The forecast saying a different generation is being built, which the deployment refuses rather than starts beside.</summary>
    internal const string DifferentReindexRunning = nameof(DifferentReindexRunning);

    /// <summary>Gets whether activating would begin a paid reindex, which is what the confirmation is asked for.</summary>
    /// <remarks>
    /// Written as the three forecasts that spend nothing rather than as the one that does, so an unrecognized forecast
    /// falls on the side that asks. The refused one is among them because the deployment turns that activation down on
    /// arrival, and a question whose only answer leads to a refusal is worse than no question.
    /// </remarks>
    internal bool WouldSpend => this.Forecast switch
    {
        AlreadyServing or WouldResumeReindex or DifferentReindexRunning => false,
        _ => true,
    };

    /// <summary>Describes what activating would do, in one line an operator reads.</summary>
    /// <returns>The sentence naming the outcome the deployment forecast.</returns>
    internal string DescribeForecast() => this.Forecast switch
    {
        WouldStartReindex => "This deployment is not embedding under that model, so activating starts a reindex.",
        WouldResumeReindex => "That model is already the generation being built, so activating leaves the reindex running.",
        AlreadyServing => "That model is already the generation searches are answered from, so activating changes nothing.",
        DifferentReindexRunning =>
            "A reindex into a different generation is running, so activating this one is refused until it is cancelled.",
        _ => $"The deployment forecast '{this.Forecast}', which this version of the command does not recognize.",
    };
}

/// <summary>What one activation did, as the deployment reports it.</summary>
/// <param name="Outcome">What the activation did.</param>
/// <param name="ProfileId">The generation the outcome is about.</param>
/// <param name="Estimate">What the run was weighed as immediately before it happened.</param>
internal sealed record EmbeddingActivation(
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("profileId")] Guid ProfileId,
    [property: JsonPropertyName("estimate")] EmbeddingWorkload? Estimate)
{
    /// <summary>Describes what the deployment started, in one line an operator reads.</summary>
    /// <returns>The sentence naming what happened and what follows from it.</returns>
    /// <remarks>
    /// Written from the outcome the deployment reported rather than from the forecast this command showed, because a
    /// reindex that started between the two is a different answer and the one that actually happened is the true one.
    /// </remarks>
    internal string Describe() => this.Outcome switch
    {
        "ReindexStarted" =>
            $"Registered generation {this.ProfileId} and started a reindex. Searches keep being answered from whatever was serving until it completes.",
        "AlreadyBuilding" =>
            $"Generation {this.ProfileId} was already being built, so the reindex was left running and its vector index re-checked.",
        "AlreadyServing" =>
            $"Generation {this.ProfileId} is already the one searches are answered from. Nothing was started and nothing was spent.",
        _ => $"The deployment reported '{this.Outcome}' for generation {this.ProfileId}.",
    };
}

/// <summary>What asking a deployment to stop its reindex did.</summary>
/// <param name="Outcome">Whether a reindex was abandoned, or none was running.</param>
internal sealed record EmbeddingReindexCancellation(
    [property: JsonPropertyName("outcome")] string? Outcome)
{
    /// <summary>Describes what the deployment did, in one line an operator reads.</summary>
    /// <returns>The sentence naming what happened.</returns>
    internal string Describe() => this.Outcome switch
    {
        "Cancelled" =>
            "Stopped the reindex. The generation it was filling is abandoned, its partial vectors are being removed, and whatever was serving goes on serving.",
        "NothingBuilding" =>
            "No reindex was running, so nothing was stopped. A run that finished before this arrived took its generation into service.",
        _ => $"The deployment reported '{this.Outcome}'.",
    };
}
