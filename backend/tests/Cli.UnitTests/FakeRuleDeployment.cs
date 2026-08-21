// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the rule routes, as the commands meet one without a server.</summary>
/// <remarks>
/// Routed by path and method rather than scripted in order, because the run path is one resource read with a get and
/// asked for with a post: what is worth asserting about <c>rules run-status</c> is that it sent no post at all, and a
/// script answering in sequence would hand the start's answer to the reading and hide exactly that.
/// </remarks>
internal static class FakeRuleDeployment
{
    /// <summary>Builds a deployment that answers each rule route with a body the caller supplies.</summary>
    /// <param name="rules">The body the rule-set route answers with.</param>
    /// <param name="runStart">The body the run route answers a post with, or a refusal.</param>
    /// <param name="runState">The body the run route answers a get with.</param>
    /// <param name="history">The body the history route answers with, or a refusal.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// The session route is answered unconditionally, because every command settles the two versions there before its
    /// own operation and a double serving the rule routes alone would report a deployment nothing can be administered
    /// on.
    /// </remarks>
    internal static FakeHttpMessageHandler Answering(
        string? rules = null,
        (HttpStatusCode Status, string Body)? runStart = null,
        string? runState = null,
        (HttpStatusCode Status, string Body)? history = null) =>
        new((request, _) => Task.FromResult(Answer(request, rules, runStart, runState, history)));

    /// <summary>Reports how often the command asked the deployment to start a run.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of run requests it sent.</returns>
    /// <remarks>
    /// The assertion the run commands are really about. A reading has to start nothing, and an operator asking twice has
    /// to reach one walk of the mailbox rather than two, so what is counted is the write rather than the answer.
    /// </remarks>
    internal static int RunRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.RuleRunsPath);
    }

    /// <summary>Reports the query string the command composed for the history it last asked for.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query, without the leading question mark, or <see langword="null" /> where none was asked for.</returns>
    internal static string? LastHistoryQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.RuleHistoryPath)?
            .RequestUri?
            .Query
            .TrimStart('?');
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string? rules,
        (HttpStatusCode Status, string Body)? runStart,
        string? runState,
        (HttpStatusCode Status, string Body)? history)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.RulesPath && rules is { } ruleSetBody)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, ruleSetBody);
        }

        if (path == AdminEndpointRoutes.RuleRunsPath && request.Method == HttpMethod.Post && runStart is { } started)
        {
            return FakeAdminEndpoint.Json(started.Status, started.Body);
        }

        if (path == AdminEndpointRoutes.RuleRunsPath && request.Method == HttpMethod.Get && runState is { } stateBody)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, stateBody);
        }

        if (path == AdminEndpointRoutes.RuleHistoryPath && history is { } answered)
        {
            return FakeAdminEndpoint.Json(answered.Status, answered.Body);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }
}
