// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the dead-letter routes, as the job commands meet one without a server.</summary>
internal static class FakeJobDeployment
{
    /// <summary>Builds a deployment answering each dead-letter route with the body the caller scripted.</summary>
    /// <param name="deadLetters">What the reading route answers, or <see langword="null" /> to serve an empty page.</param>
    /// <param name="retry">What the retry route answers, or <see langword="null" /> to answer as accepted.</param>
    /// <param name="drop">What the drop route answers, or <see langword="null" /> to answer as accepted.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// The session route is answered unconditionally, because every command settles the two versions there before its
    /// own operation and a double serving the job routes alone would report a deployment nothing can be administered
    /// on.
    /// </remarks>
    internal static FakeHttpMessageHandler Serving(
        (HttpStatusCode Status, string Body)? deadLetters = null,
        (HttpStatusCode Status, string Body)? retry = null,
        (HttpStatusCode Status, string Body)? drop = null) =>
        new((request, _) => Task.FromResult(Answer(request, deadLetters, retry, drop)));

    /// <summary>Writes the body a page of dead letters answers with.</summary>
    /// <param name="jobs">The jobs the page holds, each written by <see cref="DeadLetter" />.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Page(params string[] jobs) => (
        HttpStatusCode.OK,
        $$"""{"jobs":[{{string.Join(',', jobs)}}],"nextCursor":null}""");

    /// <summary>Writes one dead letter as the deployment reports it.</summary>
    /// <param name="job">The identifier a decision names the job by.</param>
    /// <param name="failureReason">The deployment's own name for what ended it.</param>
    /// <returns>The job, as a JSON object.</returns>
    internal static string DeadLetter(Guid job, string failureReason = "PayloadUnreadable") =>
        $$"""
          {"job":"{{job:D}}","type":"classify-email-spam","key":"account:work|email:1","account":"work",
           "attemptCount":5,"failureClassification":"Permanent","failureReason":"{{failureReason}}",
           "enqueuedAt":"2026-08-13T09:00:00+00:00","deadLetteredAt":"2026-08-13T09:30:00+00:00"}
          """;

    /// <summary>Writes the body a decision answers with.</summary>
    /// <param name="job">The job the decision named.</param>
    /// <param name="outcome">What the deployment reports happened.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Decision(Guid job, string outcome) => (
        HttpStatusCode.OK,
        $$"""{"job":"{{job:D}}","outcome":"{{outcome}}"}""");

    /// <summary>Reports how often the command asked the deployment for a decision on the named route.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="path">The administrative route the decision is asked for on.</param>
    /// <returns>The number of requests it sent there.</returns>
    internal static int DecisionRequestCount(this FakeHttpMessageHandler deployment, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == path);
    }

    /// <summary>Reports the query string the command read the dead letters with.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query string, or <see langword="null" /> where nothing was read.</returns>
    internal static string? LastDeadLetterQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.JobDeadLettersPath)?
            .RequestUri?.Query;
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        (HttpStatusCode Status, string Body)? deadLetters,
        (HttpStatusCode Status, string Body)? retry,
        (HttpStatusCode Status, string Body)? drop)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.JobRetryPath)
        {
            return Json(retry ?? (HttpStatusCode.OK, """{"job":"00000000-0000-0000-0000-000000000000","outcome":"Accepted"}"""));
        }

        if (path == AdminEndpointRoutes.JobDropPath)
        {
            return Json(drop ?? (HttpStatusCode.OK, """{"job":"00000000-0000-0000-0000-000000000000","outcome":"Accepted"}"""));
        }

        if (path == AdminEndpointRoutes.JobDeadLettersPath)
        {
            return Json(deadLetters ?? Page());
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Json((HttpStatusCode Status, string Body) answer) =>
        FakeAdminEndpoint.Json(answer.Status, answer.Body);
}
