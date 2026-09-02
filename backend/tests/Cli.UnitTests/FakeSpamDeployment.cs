// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the spam routes, as the commands meet one without a server.</summary>
/// <remarks>
/// Routed by path and method rather than scripted in order, for the reason the rule double is: the run path is one
/// resource read with a get and asked for with a post, and what is worth asserting about <c>spam run-status</c> is that
/// it sent no post at all.
/// </remarks>
internal static class FakeSpamDeployment
{
    /// <summary>Builds a deployment that answers each spam route with a body the caller supplies.</summary>
    /// <param name="runStart">The body the run route answers a post with, or a refusal.</param>
    /// <param name="runState">The body the run route answers a get with.</param>
    /// <param name="classifications">The body the classifications route answers with, or a refusal.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Answering(
        (HttpStatusCode Status, string Body)? runStart = null,
        string? runState = null,
        (HttpStatusCode Status, string Body)? classifications = null) =>
        new((request, _) => Task.FromResult(Answer(request, runStart, runState, classifications)));

    /// <summary>Reports how often the command asked the deployment to start a classification run.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of run requests it sent.</returns>
    internal static int ClassificationRunRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.SpamClassificationRunsPath);
    }

    /// <summary>Reports the query string the command composed for the classifications it last asked for.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query, without the leading question mark, or <see langword="null" /> where none was asked for.</returns>
    internal static string? LastClassificationsQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.SpamClassificationsPath)?
            .RequestUri?
            .Query
            .TrimStart('?');
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        (HttpStatusCode Status, string Body)? runStart,
        string? runState,
        (HttpStatusCode Status, string Body)? classifications)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.SpamClassificationRunsPath
            && request.Method == HttpMethod.Post
            && runStart is { } started)
        {
            return FakeAdminEndpoint.Json(started.Status, started.Body);
        }

        if (path == AdminEndpointRoutes.SpamClassificationRunsPath
            && request.Method == HttpMethod.Get
            && runState is { } stateBody)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, stateBody);
        }

        if (path == AdminEndpointRoutes.SpamClassificationsPath && classifications is { } answered)
        {
            return FakeAdminEndpoint.Json(answered.Status, answered.Body);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }
}
