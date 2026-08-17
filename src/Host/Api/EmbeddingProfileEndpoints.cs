// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the three things an operator does to this deployment's embedding profile.</summary>
/// <remarks>
/// <para>
/// Reading where semantic search stands, taking up what configuration declares, and stopping a reindex that is running.
/// Nothing here takes a model, a provider, or a width as an argument:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes configuration the place a vector space is declared and reviewed, and leaves these routes the imperative half —
/// the act that materializes a declaration and starts spending on it.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because none of them is anything a model reasons over, and because what
/// bounds administrative access is what should bound the ability to start a provider bill. <strong>Activating is
/// published under <c>mailfathom.admin.spend</c> and nothing else is</strong>, because it is the one operation on this
/// surface that begins spending somebody's money: reading the same assessment requires only
/// <c>mailfathom.admin.read</c>, so an operator can provision a credential that reports what an activation would cost
/// and cannot perform one.
/// </para>
/// <para>
/// Nothing any of them answers with is mail. Model names, counts, character totals, timestamps, and a profile
/// identifier are the whole of it.
/// </para>
/// </remarks>
internal static class EmbeddingProfileEndpoints
{
    /// <summary>The route reporting where semantic search stands, relative to the administrative prefix.</summary>
    internal const string StatusRoute = "/embeddings";

    /// <summary>The route an activation is read from and performed through, relative to the administrative prefix.</summary>
    internal const string ActivationRoute = "/embeddings/activation";

    /// <summary>The route that stops the reindex under way, relative to the administrative prefix.</summary>
    internal const string ReindexCancellationRoute = "/embeddings/reindex/cancellation";

    /// <summary>Maps the embedding routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapEmbeddingProfile(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(StatusRoute, ReadStatusAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // One path and two verbs, because they are one operation read and then performed: what the reading answers is
        // exactly what the write will weigh, so an operator confirming a figure and a deployment refusing one are
        // talking about the same thing rather than about two endpoints that happen to count alike. The two grants
        // differ for the same reason, since only one of them starts a bill.
        api.MapGet(ActivationRoute, ReadActivationAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(ActivationRoute, ActivateAsync)
            .RequirePermission(MailFathomPermission.AdminSpend);

        api.MapPost(ReindexCancellationRoute, CancelReindexAsync)
            .RequirePermission(MailFathomPermission.AdminOperate);
    }

    /// <summary>Reports whether semantic search is working on this instance, and how far behind it is.</summary>
    /// <param name="declared">What this deployment declares it embeds with, which may be nothing.</param>
    /// <param name="reader">Composes the answer from the generations, the counts, the provider, and the budget.</param>
    /// <param name="cancellationToken">Cancels the reads when the client disconnects.</param>
    /// <returns><c>200</c> with the state on every instance including one that declared no provider, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.admin.read</c>.</returns>
    /// <remarks>
    /// The grant is the only thing it refuses over. An instance with no declaration, no activation, and no provider is a
    /// supported deployment serving lexical search, and it is also the instance whose operator is most likely to be
    /// asking this question — so the absence of every part is the answer rather than an error.
    /// </remarks>
    internal static async Task<Ok<EmbeddingStatusResponse>> ReadStatusAsync(
        [FromServices] DeclaredEmbeddingGeometry declared,
        [FromServices] EmbeddingStatusReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(reader);

        var status = await reader.ReadAsync(declared.Identity, cancellationToken);

        return TypedResults.Ok(EmbeddingStatusResponse.For(status));
    }

    /// <summary>Reports what activating the declared geometry would do and what it would cost, writing nothing.</summary>
    /// <param name="declared">What this deployment declares it embeds with.</param>
    /// <param name="activation">Counts what the activation would send and reads the budget it is weighed against.</param>
    /// <param name="cancellationToken">Cancels the reads when the client disconnects.</param>
    /// <returns><c>200</c> with the assessment, or <c>400</c> when this deployment declares no embedding provider.</returns>
    internal static async Task<Results<Ok<EmbeddingActivationAssessmentResponse>, ProblemHttpResult>> ReadActivationAsync(
        [FromServices] DeclaredEmbeddingGeometry declared,
        [FromServices] CountedEmbeddingActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(activation);

        if (declared.Identity is not { } geometry)
        {
            return NothingDeclared();
        }

        var assessment = await activation.AssessAsync(geometry, cancellationToken);

        return TypedResults.Ok(EmbeddingActivationAssessmentResponse.For(assessment));
    }

    /// <summary>Takes up the declared geometry, unless the budget or a running reindex refuses it.</summary>
    /// <param name="declared">What this deployment declares it embeds with.</param>
    /// <param name="activation">Weighs the estimate against the ceiling and registers the generation.</param>
    /// <param name="cancellationToken">Cancels the reads and the registration when the client disconnects.</param>
    /// <returns><c>200</c> with what it did, <c>400</c> when nothing is declared, or <c>409</c> when the spend ceiling or a running reindex refuses it.</returns>
    /// <remarks>
    /// The two refusals are <c>409</c> rather than <c>400</c> because neither is a mistake in the request: the caller
    /// asked for the only activation this deployment has, and what refused it is the state the deployment is in. Both
    /// name what an operator has to change, which for the ceiling is the two numbers it was weighed as.
    /// </remarks>
    internal static async Task<Results<Ok<EmbeddingActivationResponse>, ProblemHttpResult>> ActivateAsync(
        [FromServices] DeclaredEmbeddingGeometry declared,
        [FromServices] CountedEmbeddingActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(activation);

        if (declared.Identity is not { } geometry)
        {
            return NothingDeclared();
        }

        var result = await activation.ActivateAsync(geometry, cancellationToken);

        if (result.Activation is not { } performed)
        {
            return Refused(DescribeCeilingRefusal(result.Assessment));
        }

        if (performed.Outcome == EmbeddingProfileActivationOutcome.DifferentReindexRunning)
        {
            return Refused(
                "A reindex into a different generation is already running, and one reindex runs at a time. Cancel it "
                + "before activating this declaration; the generation it was filling is abandoned and whatever was "
                + "spent on it is spent.");
        }

        return TypedResults.Ok(new EmbeddingActivationResponse(
            performed.Outcome.ToString(),
            performed.ProfileId.Value,
            EmbeddingWorkloadResponse.For(result.Assessment.Estimate)));
    }

    /// <summary>Stops the reindex under way, leaving the generation that is serving exactly where it is.</summary>
    /// <param name="cancellation">Abandons the generation being built.</param>
    /// <param name="cancellationToken">Cancels the read and the transition when the client disconnects.</param>
    /// <returns><c>200</c> saying whether a reindex was abandoned or none was running.</returns>
    /// <remarks>
    /// Finding nothing to cancel is an outcome rather than a refusal. A reindex that completed between the operator
    /// deciding to stop it and the request arriving took its generation into service, and reporting that as an error
    /// would say something went wrong when what happened is that the run finished.
    /// </remarks>
    internal static async Task<Ok<EmbeddingReindexCancellationResponse>> CancelReindexAsync(
        [FromServices] EmbeddingReindexCancellation cancellation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cancellation);

        var outcome = await cancellation.CancelAsync(cancellationToken);

        return TypedResults.Ok(new EmbeddingReindexCancellationResponse(outcome.ToString()));
    }

    /// <summary>States the two numbers a refused activation was weighed as, and what to change.</summary>
    /// <remarks>
    /// The estimate and the ceiling both, because either alone leaves the operator guessing at the other. ADR 0006
    /// refuses rather than paces here deliberately: a ceiling that only slowed a run down would be a schedule, so the
    /// way past this is to raise the figure the deployment agreed to.
    /// </remarks>
    private static string DescribeCeilingRefusal(EmbeddingActivationAssessment assessment) =>
        $"Activating the declared model would send {assessment.Estimate.OutstandingCharacterCount} characters to the "
        + $"provider, and this deployment admits at most {assessment.Period.CeilingInputCharacterCount} in each "
        + $"{assessment.Period.EndsAt - assessment.Period.StartsAt} period. Raise "
        + "'Embeddings:MaxInputCharactersPerPeriod', or set it to zero to declare no ceiling at all, and activate again.";

    private static ProblemHttpResult NothingDeclared() => TypedResults.Problem(
        "This deployment declares no embedding provider, so there is nothing to activate. Declare one under "
        + "'Embeddings:Endpoints' and restart before activating.",
        statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict);
}
