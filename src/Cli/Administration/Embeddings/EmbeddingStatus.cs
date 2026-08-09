// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Embeddings;

/// <summary>What a deployment reports about where its semantic search stands.</summary>
/// <remarks>
/// Every part is optional on the wire, and none of the absences is a fault: a deployment that declared no provider
/// sends no geometry, one that has activated nothing sends no generation, and one that has never called a provider
/// sends a state saying nothing has been observed. The command reads each absence as the answer it is rather than as a
/// malformed response.
/// </remarks>
/// <param name="Declared">The vector space configuration declares, or <see langword="null" /> where it declares none.</param>
/// <param name="ActivationOutstanding">Whether that declaration is waiting for an activation nobody has performed.</param>
/// <param name="Serving">The generation searches are answered from, or <see langword="null" /> when the deployment has activated none.</param>
/// <param name="Building">The generation a reindex is filling, or <see langword="null" /> when no reindex is running.</param>
/// <param name="Provider">What the deployment's last call to its embedding provider established.</param>
/// <param name="Spend">Where the deployment's budget period stands.</param>
/// <param name="NextBackfillPassDueAt">When the deployment's backfill runs its next pass, or <see langword="null" /> while it has scheduled none.</param>
internal sealed record EmbeddingStatus(
    [property: JsonPropertyName("declared")] EmbeddingGeometry? Declared,
    [property: JsonPropertyName("activationOutstanding")] bool ActivationOutstanding,
    [property: JsonPropertyName("serving")] EmbeddingGeneration? Serving,
    [property: JsonPropertyName("building")] EmbeddingGeneration? Building,
    [property: JsonPropertyName("provider")] EmbeddingProviderHealth? Provider,
    [property: JsonPropertyName("spend")] EmbeddingSpend? Spend,
    [property: JsonPropertyName("nextBackfillPassDueAt")] DateTimeOffset? NextBackfillPassDueAt);

/// <summary>One vector space, as a deployment names it.</summary>
/// <param name="Fingerprint">The digest the deployment's profile row is unique on.</param>
/// <param name="Provider">The vendor whose model defines the space.</param>
/// <param name="Model">The model identifier the provider publishes.</param>
/// <param name="ModelVersion">The model version the provider exposes, or <see langword="null" /> where it exposes none.</param>
/// <param name="Dimension">The width of the vectors this space holds.</param>
/// <param name="DistanceMetric">How distance is measured between two of its vectors.</param>
internal sealed record EmbeddingGeometry(
    [property: JsonPropertyName("fingerprint")] string? Fingerprint,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("modelVersion")] string? ModelVersion,
    [property: JsonPropertyName("dimension")] int Dimension,
    [property: JsonPropertyName("distanceMetric")] string? DistanceMetric)
{
    /// <summary>Describes this space in one line an operator reads.</summary>
    /// <returns>The provider, the model, its version where the provider exposes one, the width, and the metric.</returns>
    internal string Describe()
    {
        var version = string.IsNullOrWhiteSpace(this.ModelVersion) ? string.Empty : $" ({this.ModelVersion})";

        return $"{this.Provider} {this.Model}{version}, {this.Dimension} dimensions, {this.DistanceMetric}";
    }
}

/// <summary>One of a deployment's generations, and how far it has come.</summary>
/// <param name="ProfileId">The profile row the vectors of this generation hang on.</param>
/// <param name="Geometry">The vector space it fixed at registration.</param>
/// <param name="Progress">What it still owes.</param>
internal sealed record EmbeddingGeneration(
    [property: JsonPropertyName("profileId")] Guid ProfileId,
    [property: JsonPropertyName("geometry")] EmbeddingGeometry? Geometry,
    [property: JsonPropertyName("progress")] EmbeddingWorkload? Progress);

/// <summary>What one of a deployment's vector spaces still owes.</summary>
/// <param name="SearchableEmailCount">The messages a search may reach at all.</param>
/// <param name="EmbeddedEmailCount">How many of those this vector space already covers.</param>
/// <param name="OutstandingEmailCount">How many of those it does not.</param>
/// <param name="OutstandingPassageCount">The passages that would be sent to a provider.</param>
/// <param name="OutstandingCharacterCount">The characters those passages carry.</param>
/// <param name="ApproximateTokenCount">Those characters expressed as tokens.</param>
/// <remarks>
/// Every figure is grouped invariantly rather than for the terminal's culture. The published binary sets
/// <c>InvariantGlobalization</c>, so a culture-sensitive format would render one way in a test host and another in the
/// tool itself — and these numbers are the ones an operator quotes back when asking why an activation was refused.
/// </remarks>
internal sealed record EmbeddingWorkload(
    [property: JsonPropertyName("searchableEmailCount")] int SearchableEmailCount,
    [property: JsonPropertyName("embeddedEmailCount")] int EmbeddedEmailCount,
    [property: JsonPropertyName("outstandingEmailCount")] int OutstandingEmailCount,
    [property: JsonPropertyName("outstandingPassageCount")] long OutstandingPassageCount,
    [property: JsonPropertyName("outstandingCharacterCount")] long OutstandingCharacterCount,
    [property: JsonPropertyName("approximateTokenCount")] long ApproximateTokenCount)
{
    /// <summary>Describes how much of the mailbox this space covers, in one line an operator reads.</summary>
    /// <returns>The two message counts, and what is left to send where anything is.</returns>
    internal string DescribeProgress()
    {
        var covered = string.Create(
            CultureInfo.InvariantCulture,
            $"{this.EmbeddedEmailCount:N0} of {this.SearchableEmailCount:N0} messages embedded");

        return this.OutstandingPassageCount == 0
            ? $"{covered}; nothing outstanding"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{covered}; {this.OutstandingPassageCount:N0} passages left ({this.DescribeCost()})");
    }

    /// <summary>Describes what the outstanding passages would be charged as, in the terms a provider prices in.</summary>
    /// <returns>The characters and the approximate tokens they stand for.</returns>
    /// <remarks>Both, because the character count is what this deployment counts exactly and the token count is what the provider bills, approximately.</remarks>
    internal string DescribeCost() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.OutstandingCharacterCount:N0} characters, roughly {this.ApproximateTokenCount:N0} tokens");
}

/// <summary>What a deployment's last call to its embedding provider established about it.</summary>
/// <param name="State">What that call established.</param>
/// <param name="ObservedAt">When it ended, or <see langword="null" /> while nothing has been observed.</param>
internal sealed record EmbeddingProviderHealth(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("observedAt")] DateTimeOffset? ObservedAt);

/// <summary>Where a deployment's embedding budget period stands.</summary>
/// <param name="PeriodStartsAt">When the period began.</param>
/// <param name="PeriodEndsAt">When it rolls over, which is when paused embedding resumes.</param>
/// <param name="ConsumedInputCharacterCount">The characters already sent inside this period.</param>
/// <param name="CeilingInputCharacterCount">The characters the period admits, or <see langword="null" /> where the deployment declared no ceiling.</param>
/// <param name="RemainingInputCharacterCount">What the period still admits, or <see langword="null" /> where nothing is counted against.</param>
internal sealed record EmbeddingSpend(
    [property: JsonPropertyName("periodStartsAt")] DateTimeOffset PeriodStartsAt,
    [property: JsonPropertyName("periodEndsAt")] DateTimeOffset PeriodEndsAt,
    [property: JsonPropertyName("consumedInputCharacterCount")] long ConsumedInputCharacterCount,
    [property: JsonPropertyName("ceilingInputCharacterCount")] long? CeilingInputCharacterCount,
    [property: JsonPropertyName("remainingInputCharacterCount")] long? RemainingInputCharacterCount)
{
    /// <summary>Describes the period in one line an operator reads.</summary>
    /// <returns>What has been spent, against the ceiling where one was declared, and when the period rolls over.</returns>
    internal string Describe()
    {
        var spent = this.CeilingInputCharacterCount is { } ceiling
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{this.ConsumedInputCharacterCount:N0} of {ceiling:N0} characters")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{this.ConsumedInputCharacterCount:N0} characters, against no declared ceiling");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{spent}; the period rolls over at {this.PeriodEndsAt:u}");
    }
}
