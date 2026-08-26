// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the three things an operator does about background work that stopped.</summary>
/// <remarks>
/// <para>
/// Reading what has dead-lettered, running one again after fixing what caused it, and recording that one will never be
/// run. Together they are what makes a terminal state a decision point rather than an accumulation: without them a
/// dead letter is a row nobody can see and nobody can act on, and the queue's only remedy is a database client.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because none of them is anything a model reasons over, and because
/// re-running work that changes somebody's mailbox should be bounded by the credential that bounds everything else
/// administrative. Reading what stopped is <c>mailfathom.admin.read</c> and both decisions are
/// <c>mailfathom.admin.operate</c>, so a monitoring credential can report a queue it cannot act on.
/// </para>
/// <para>
/// Nothing any of them answers with is mail. A job type's name, an idempotency key composed of MailFathom's own
/// aliases and identifiers, an account alias, counts, instants, and the operator-safe record of what failed are the
/// whole of it — the payload naming the message the work is about is not read from the row at all.
/// </para>
/// </remarks>
internal static class JobDeadLetterEndpoints
{
    /// <summary>The route the jobs nothing will attempt again are read from, relative to the administrative prefix.</summary>
    internal const string DeadLettersRoute = "/jobs/dead-letters";

    /// <summary>The route one dead letter is asked to be run again on, relative to the administrative prefix.</summary>
    internal const string RetryRoute = "/jobs/dead-letters/retry";

    /// <summary>The route one dead letter is recorded as never to be run on, relative to the administrative prefix.</summary>
    internal const string DropRoute = "/jobs/dead-letters/drop";

    /// <summary>The greatest request body either decision route reads before refusing it.</summary>
    /// <remarks>
    /// The body names one job and nothing else, so a few hundred bytes is the whole of anything it could mean. Stated
    /// for the reason every other administrative write states it: the server's own default is measured in tens of
    /// megabytes, which here would let an authenticated client make the process buffer a body four orders of magnitude
    /// larger than the request it is sending.
    /// </remarks>
    internal const int MaxDecisionRequestBytes = 4 * 1024;

    /// <summary>Maps the dead-letter routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapJobDeadLetters(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(DeadLettersRoute, ReadDeadLettersAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the erasure route reaches
        // it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature,
        // so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(RetryRoute, RetryAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapPost(DropRoute, DropAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxDecisionRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);
    }

    /// <summary>Serves one page of the jobs nothing will attempt again, newest first.</summary>
    /// <param name="type">The job type to narrow to, or <see langword="null" /> for every type.</param>
    /// <param name="account">The configured identifier of the account to narrow to, or <see langword="null" /> for every account.</param>
    /// <param name="pageSize">How many jobs the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="deadLetters">Reads the page.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure, which mirrors the other
    /// paged administrative readings: an unknown account is a mistake in the request the caller wrote rather than a
    /// missing resource, and <c>404</c> is already what a client reads as "this port serves no administrative endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<DeadLetteredJobPageResponse>, ProblemHttpResult>> ReadDeadLettersAsync(
        [FromQuery] string? type,
        [FromQuery] string? account,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] DeadLetteredJobs deadLetters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(deadLetters);

        if (!AdminAccountRequest.TryResolveFilter(account, accounts, out var servedAccount, out var refusal))
        {
            return refusal;
        }

        JobType? jobType = null;

        if (type is not null)
        {
            if (!JobType.TryParseName(type, out var namedType))
            {
                return TypedResults.Problem(
                    $"The type filter names no job type this deployment runs. It is one of {DeclaredJobTypes()}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            jobType = namedType;
        }

        DeadLetteredJobCursor? decodedCursor = null;

        if (cursor is not null && !DeadLetteredJobCursor.TryDecode(cursor, out decodedCursor))
        {
            return TypedResults.Problem(
                "The continuation cursor is not one this deployment issued.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queryResult = DeadLetteredJobQuery.Create(jobType, servedAccount, pageSize, decodedCursor);

        if (queryResult.Query is not { } query)
        {
            return TypedResults.Problem(
                DescribeRefusal(queryResult.Outcome),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await deadLetters.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(DeadLetteredJobPageResponse.For(page));
    }

    /// <summary>Returns one dead letter to the queue, to be run again under the identity it already carries.</summary>
    /// <param name="request">The job to attempt again.</param>
    /// <param name="deadLetters">Performs the decision.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A job that is not dead-lettered is an outcome rather than a refusal, so it is answered <c>200</c> with that
    /// outcome named: the caller asked a question this deployment can answer, and the answer is that the job had already
    /// moved on — which is exactly what a second operator acting on a list a moment old needs to be told, rather than a
    /// status they have to interpret. The same holds of a job this deployment does not hold.
    /// </remarks>
    internal static async Task<Results<Ok<JobRecoveryResponse>, ProblemHttpResult>> RetryAsync(
        [FromBody] JobRecoveryRequest? request,
        [FromServices] DeadLetteredJobs deadLetters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deadLetters);

        if (ResolveJob(request?.Job) is not { } jobId)
        {
            return NoJobNamed();
        }

        var outcome = await deadLetters.RetryAsync(jobId, cancellationToken);

        return TypedResults.Ok(JobRecoveryResponse.For(jobId, outcome));
    }

    /// <summary>Records that one dead letter will never be run, leaving the row and its failure where they are.</summary>
    /// <param name="request">The job to drop.</param>
    /// <param name="deadLetters">Performs the decision.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what happened, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>The record is kept rather than removed, for the reason <see cref="JobState.Dropped" /> gives: what an operator decided about a job is itself worth keeping.</remarks>
    internal static async Task<Results<Ok<JobRecoveryResponse>, ProblemHttpResult>> DropAsync(
        [FromBody] JobRecoveryRequest? request,
        [FromServices] DeadLetteredJobs deadLetters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deadLetters);

        if (ResolveJob(request?.Job) is not { } jobId)
        {
            return NoJobNamed();
        }

        var outcome = await deadLetters.DropAsync(jobId, cancellationToken);

        return TypedResults.Ok(JobRecoveryResponse.For(jobId, outcome));
    }

    /// <summary>Reads the job a request named, keeping an absent identifier apart from an unusable one.</summary>
    private static JobId? ResolveJob(Guid? job) => job is { } identifier && identifier != Guid.Empty
        ? JobId.Create(identifier)
        : null;

    private static string DeclaredJobTypes() => string.Join(", ", JobType.All.Select(declared => declared.Name));

    /// <summary>States that the request named no job to decide about.</summary>
    private static ProblemHttpResult NoJobNamed() => TypedResults.Problem(
        "The request named no job. Name the identifier the dead-letter reading reports for it.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(DeadLetteredJobQueryOutcome outcome) => outcome switch
    {
        DeadLetteredJobQueryOutcome.PageSizeOutOfRange =>
            $"A page of dead letters holds between 1 and {DeadLetteredJobQuery.MaximumPageSize} records.",
        DeadLetteredJobQueryOutcome.JobTypeUnknown =>
            $"The type filter names no job type this deployment runs. It is one of {DeclaredJobTypes()}.",
        _ => "The continuation cursor was issued for a different set of dead-letter filters.",
    };
}

/// <summary>What a deployment is asked when one dead letter is to be run again or dropped.</summary>
/// <param name="Job">The identifier the dead-letter reading reports for the job.</param>
internal sealed record JobRecoveryRequest(Guid? Job);

/// <summary>One page of the jobs a deployment will not attempt again.</summary>
/// <param name="Jobs">The jobs, ordered by when each one stopped, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record DeadLetteredJobPageResponse(
    IReadOnlyList<DeadLetteredJobResponse> Jobs,
    string? NextCursor)
{
    /// <summary>Describes one page as the administrative surface reports it.</summary>
    /// <param name="page">The page read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static DeadLetteredJobPageResponse For(DeadLetteredJobPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new DeadLetteredJobPageResponse(
            [.. page.Jobs.Select(DeadLetteredJobResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One job nothing will attempt again.</summary>
/// <param name="Job">The identifier a retry or a drop names it by.</param>
/// <param name="Type">The kind of work.</param>
/// <param name="Key">The identity the enqueuer composed, which a retry runs under unchanged.</param>
/// <param name="Account">The account the work belongs to, absent when it belongs to none.</param>
/// <param name="AttemptCount">How many attempts were handed out before the job stopped.</param>
/// <param name="FailureClassification">What the failure that ended it was classified as, absent where the row records none.</param>
/// <param name="FailureReason">The operator-safe name of that failure, absent where the row records none.</param>
/// <param name="EnqueuedAt">When the work was first enqueued.</param>
/// <param name="DeadLetteredAt">When the job stopped.</param>
internal sealed record DeadLetteredJobResponse(
    Guid Job,
    string Type,
    string Key,
    string? Account,
    int AttemptCount,
    string? FailureClassification,
    string? FailureReason,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset DeadLetteredAt)
{
    /// <summary>Describes one dead letter as the administrative surface reports it.</summary>
    /// <param name="job">The job read.</param>
    /// <returns>The response record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="job" /> is <see langword="null" />.</exception>
    internal static DeadLetteredJobResponse For(DeadLetteredJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new DeadLetteredJobResponse(
            job.JobId.Value,
            job.JobType.Name,
            job.Key.Value,
            job.AccountId?.Value,
            job.AttemptCount,
            job.LastFailure?.Classification.ToString(),
            job.LastFailure?.Reason,
            job.EnqueuedAt,
            job.DeadLetteredAt);
    }
}

/// <summary>What became of a job an operator decided about.</summary>
/// <param name="Job">The job the decision named.</param>
/// <param name="Outcome">What happened: <c>Accepted</c>, <c>JobUnknown</c>, or <c>JobNotDeadLettered</c>.</param>
internal sealed record JobRecoveryResponse(Guid Job, string Outcome)
{
    /// <summary>Describes one decision as the administrative surface reports it.</summary>
    /// <param name="jobId">The job the decision named.</param>
    /// <param name="outcome">What happened.</param>
    /// <returns>The response body.</returns>
    internal static JobRecoveryResponse For(JobId jobId, JobRecoveryOutcome outcome) =>
        new(jobId.Value, outcome.ToString());
}
