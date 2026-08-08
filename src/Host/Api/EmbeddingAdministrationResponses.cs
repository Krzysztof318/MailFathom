// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Limits;

namespace MailFathom.Host.Api;

/// <summary>One vector space, as the administrative endpoint names it.</summary>
/// <remarks>
/// The readable half and the exact half together. Provider, model, width, and metric are what an operator recognizes;
/// the fingerprint is what the profile table is unique on, so two declarations differing only in how a passage is
/// prepared are still told apart here rather than reading as the same model twice.
/// </remarks>
/// <param name="Fingerprint">The digest the profile row is unique on.</param>
/// <param name="Provider">The vendor whose model defines the space.</param>
/// <param name="Model">The model identifier the provider publishes.</param>
/// <param name="ModelVersion">The model version the provider exposes, or <see langword="null" /> where it exposes none.</param>
/// <param name="Dimension">The width of the vectors this space holds.</param>
/// <param name="DistanceMetric">How distance is measured between two of its vectors.</param>
internal sealed record EmbeddingGeometryResponse(
    string Fingerprint,
    string Provider,
    string Model,
    string? ModelVersion,
    int Dimension,
    string DistanceMetric)
{
    /// <summary>Describes one geometry for the wire.</summary>
    /// <param name="identity">The geometry.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identity" /> is <see langword="null" />.</exception>
    internal static EmbeddingGeometryResponse For(EmbeddingProfileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return new EmbeddingGeometryResponse(
            EmbeddingProfileFingerprint.Compute(identity).Value,
            identity.Provider,
            identity.ModelIdentifier,
            identity.ModelVersion,
            identity.Dimension,
            identity.DistanceMetric.ToString());
    }
}

/// <summary>What one vector space still owes, as the administrative endpoint reports it.</summary>
/// <param name="SearchableEmailCount">The messages a search may reach at all.</param>
/// <param name="EmbeddedEmailCount">How many of those this vector space already covers.</param>
/// <param name="OutstandingEmailCount">How many of those it does not.</param>
/// <param name="OutstandingPassageCount">The passages that would be sent to a provider.</param>
/// <param name="OutstandingCharacterCount">The characters those passages carry.</param>
/// <param name="ApproximateTokenCount">Those characters expressed as tokens, which bounds the order of magnitude of a bill rather than predicting one.</param>
internal sealed record EmbeddingWorkloadResponse(
    int SearchableEmailCount,
    int EmbeddedEmailCount,
    int OutstandingEmailCount,
    long OutstandingPassageCount,
    long OutstandingCharacterCount,
    long ApproximateTokenCount)
{
    /// <summary>Describes one workload for the wire.</summary>
    /// <param name="workload">The workload.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workload" /> is <see langword="null" />.</exception>
    internal static EmbeddingWorkloadResponse For(EmbeddingWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        return new EmbeddingWorkloadResponse(
            workload.SearchableEmailCount,
            workload.EmbeddedEmailCount,
            workload.OutstandingEmailCount,
            workload.OutstandingPassageCount,
            workload.OutstandingCharacterCount,
            workload.ApproximateTokenCount);
    }
}

/// <summary>One registered generation and how far it has come, as the administrative endpoint reports it.</summary>
/// <param name="ProfileId">The profile row the vectors of this generation hang on.</param>
/// <param name="Geometry">The vector space it fixed at registration.</param>
/// <param name="Progress">What it still owes.</param>
internal sealed record EmbeddingGenerationResponse(
    Guid ProfileId,
    EmbeddingGeometryResponse Geometry,
    EmbeddingWorkloadResponse Progress)
{
    /// <summary>Describes one generation for the wire.</summary>
    /// <param name="generation">The generation, or <see langword="null" /> when this instance holds none in that state.</param>
    /// <returns>The response body, or <see langword="null" />.</returns>
    internal static EmbeddingGenerationResponse? For(EmbeddingGenerationProgress? generation) => generation is { } present
        ? new EmbeddingGenerationResponse(
            present.Profile.Id.Value,
            EmbeddingGeometryResponse.For(present.Profile.Identity),
            EmbeddingWorkloadResponse.For(present.Workload))
        : null;
}

/// <summary>What the last call to the embedding provider established, as the administrative endpoint reports it.</summary>
/// <param name="State">What that call established.</param>
/// <param name="ObservedAt">When it ended, or <see langword="null" /> while nothing has been observed.</param>
/// <remarks>
/// Observed rather than probed, which is worth knowing when reading it: nothing here calls a provider to answer the
/// question, so a deployment that has not embedded since it started reports that nothing is known rather than a
/// failure.
/// </remarks>
internal sealed record EmbeddingProviderHealthResponse(string State, DateTimeOffset? ObservedAt)
{
    /// <summary>Describes one provider's state for the wire.</summary>
    /// <param name="health">The state.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="health" /> is <see langword="null" />.</exception>
    internal static EmbeddingProviderHealthResponse For(AiProviderHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        return new EmbeddingProviderHealthResponse(health.State.ToString(), health.ObservedAt);
    }
}

/// <summary>Where the deployment's budget period stands, as the administrative endpoint reports it.</summary>
/// <param name="PeriodStartsAt">When the period began.</param>
/// <param name="PeriodEndsAt">When it rolls over, which is when paused embedding resumes.</param>
/// <param name="ConsumedInputCharacterCount">The characters already sent inside this period.</param>
/// <param name="CeilingInputCharacterCount">The characters the period admits, or <see langword="null" /> where the deployment declared no ceiling.</param>
/// <param name="RemainingInputCharacterCount">What the period still admits, or <see langword="null" /> where nothing is counted against.</param>
internal sealed record EmbeddingSpendResponse(
    DateTimeOffset PeriodStartsAt,
    DateTimeOffset PeriodEndsAt,
    long ConsumedInputCharacterCount,
    long? CeilingInputCharacterCount,
    long? RemainingInputCharacterCount)
{
    /// <summary>Describes one budget period for the wire.</summary>
    /// <param name="period">The period.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="period" /> is <see langword="null" />.</exception>
    internal static EmbeddingSpendResponse For(EmbeddingSpendPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return new EmbeddingSpendResponse(
            period.StartsAt,
            period.EndsAt,
            period.ConsumedInputCharacterCount,
            period.CeilingInputCharacterCount,
            period.RemainingInputCharacterCount);
    }
}

/// <summary>Where semantic search stands on this instance, as one answer.</summary>
/// <param name="Declared">The geometry configuration declares, or <see langword="null" /> on an instance that declared no provider.</param>
/// <param name="ActivationOutstanding">Whether the declaration is waiting for an activation nobody has performed.</param>
/// <param name="Serving">The generation searches are answered from, or <see langword="null" /> when this instance has activated none.</param>
/// <param name="Building">The generation a reindex is filling, or <see langword="null" /> when no reindex is running.</param>
/// <param name="Provider">What the last call to the embedding provider established.</param>
/// <param name="Spend">Where the budget period stands.</param>
internal sealed record EmbeddingStatusResponse(
    EmbeddingGeometryResponse? Declared,
    bool ActivationOutstanding,
    EmbeddingGenerationResponse? Serving,
    EmbeddingGenerationResponse? Building,
    EmbeddingProviderHealthResponse Provider,
    EmbeddingSpendResponse Spend)
{
    /// <summary>Describes one instance's embedding state for the wire.</summary>
    /// <param name="status">The state.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="status" /> is <see langword="null" />.</exception>
    internal static EmbeddingStatusResponse For(EmbeddingStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new EmbeddingStatusResponse(
            status.Declared is { } declared ? EmbeddingGeometryResponse.For(declared) : null,
            status.ActivationOutstanding,
            EmbeddingGenerationResponse.For(status.Serving),
            EmbeddingGenerationResponse.For(status.Building),
            EmbeddingProviderHealthResponse.For(status.ProviderHealth),
            EmbeddingSpendResponse.For(status.Period));
    }
}

/// <summary>What activating the declared geometry would do and what it would cost, before anything is written.</summary>
/// <param name="Declared">The geometry that would be activated.</param>
/// <param name="Forecast">What activating it would do.</param>
/// <param name="Estimate">What that would cost.</param>
/// <param name="Spend">Where the budget period stands.</param>
/// <param name="ExceedsSpendCeiling">Whether the declared ceiling refuses this activation outright.</param>
internal sealed record EmbeddingActivationAssessmentResponse(
    EmbeddingGeometryResponse Declared,
    string Forecast,
    EmbeddingWorkloadResponse Estimate,
    EmbeddingSpendResponse Spend,
    bool ExceedsSpendCeiling)
{
    /// <summary>Describes one assessment for the wire.</summary>
    /// <param name="assessment">The assessment.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assessment" /> is <see langword="null" />.</exception>
    internal static EmbeddingActivationAssessmentResponse For(EmbeddingActivationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        return new EmbeddingActivationAssessmentResponse(
            EmbeddingGeometryResponse.For(assessment.Declared),
            assessment.Forecast.ToString(),
            EmbeddingWorkloadResponse.For(assessment.Estimate),
            EmbeddingSpendResponse.For(assessment.Period),
            assessment.ExceedsSpendCeiling);
    }
}

/// <summary>What one activation did, as the administrative endpoint reports it.</summary>
/// <param name="Outcome">What the activation did.</param>
/// <param name="ProfileId">The generation the outcome is about.</param>
/// <param name="Estimate">What the run was weighed as immediately before it happened.</param>
/// <remarks>
/// The estimate travels with the answer so an operator can recognize the figure they confirmed in what the deployment
/// says it started, rather than having to trust that the two readings agreed.
/// </remarks>
internal sealed record EmbeddingActivationResponse(
    string Outcome,
    Guid ProfileId,
    EmbeddingWorkloadResponse Estimate);

/// <summary>What one cancellation did, as the administrative endpoint reports it.</summary>
/// <param name="Outcome">Whether a reindex was abandoned, or none was running.</param>
internal sealed record EmbeddingReindexCancellationResponse(string Outcome);
