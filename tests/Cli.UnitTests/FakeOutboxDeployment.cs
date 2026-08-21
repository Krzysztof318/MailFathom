// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the outbox routes, as the outbox commands meet one without a server.</summary>
internal static class FakeOutboxDeployment
{
    /// <summary>Builds a deployment answering each outbox route with the body the caller scripted.</summary>
    /// <param name="summary">What the summary route answers, or <see langword="null" /> to serve an empty outbox.</param>
    /// <param name="listing">What the listing route answers, or <see langword="null" /> to serve an empty page.</param>
    /// <param name="send">What the single-send route answers, or <see langword="null" /> to answer <c>404</c>.</param>
    /// <param name="cancellation">What the cancellation route answers, or <see langword="null" /> to answer as accepted.</param>
    /// <param name="requeue">What the re-queue route answers, or <see langword="null" /> to answer as accepted.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// The session route is answered unconditionally, because every command settles the two versions there before its
    /// own operation and a double serving the outbox routes alone would report a deployment nothing can be administered
    /// on.
    /// </remarks>
    internal static FakeHttpMessageHandler Serving(
        (HttpStatusCode Status, string Body)? summary = null,
        (HttpStatusCode Status, string Body)? listing = null,
        (HttpStatusCode Status, string Body)? send = null,
        (HttpStatusCode Status, string Body)? cancellation = null,
        (HttpStatusCode Status, string Body)? requeue = null) =>
        new((request, _) => Task.FromResult(Answer(request, summary, listing, send, cancellation, requeue)));

    /// <summary>Writes the body the summary route answers with.</summary>
    /// <param name="outstandingCount">How many sends the deployment reports as unfinished.</param>
    /// <param name="stages">The counts, each written as <c>stage:count</c>.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Summary(
        int outstandingCount,
        params string[] stages)
    {
        var written = stages.Select(stage => stage.Split(':'))
            .Select(parts => $$"""{"stage":"{{parts[0]}}","count":{{parts[1]}}}""");

        return (
            HttpStatusCode.OK,
            $$"""{"stages":[{{string.Join(',', written)}}],"outstandingCount":{{outstandingCount}}}""");
    }

    /// <summary>Writes the body a page of the outbox answers with.</summary>
    /// <param name="sends">The sends the page holds, each written by <see cref="Entry" />.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Page(params string[] sends) => (
        HttpStatusCode.OK,
        $$"""{"sends":[{{string.Join(',', sends)}}],"nextCursor":null}""");

    /// <summary>Writes one recorded send as the listing reports it.</summary>
    /// <param name="outgoingEmail">The identifier a decision names it by.</param>
    /// <param name="stage">The stage it stands at.</param>
    /// <returns>The send, as a JSON object.</returns>
    internal static string Entry(Guid outgoingEmail, string stage = "Recorded") =>
        $$"""
          {"outgoingEmail":"{{outgoingEmail:D}}","account":"work","stage":"{{stage}}","origin":"Command",
           "attemptCount":2,"mimeByteLength":4096,"recordedAt":"2026-08-19T09:00:00+00:00",
           "stageChangedAt":"2026-08-19T09:05:00+00:00","availableAt":"2026-08-19T09:30:00+00:00",
           "lastFailureCode":27001,"lastReplyCode":451}
          """;

    /// <summary>Writes one recorded send as the single-send reading reports it, with its recipients.</summary>
    /// <param name="outgoingEmail">The identifier a decision names it by.</param>
    /// <param name="stage">The stage it stands at.</param>
    /// <param name="recipient">The one address the send is offered to.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Send(
        Guid outgoingEmail,
        string stage = "TransmissionBegun",
        string recipient = "anna@example.test") => (
        HttpStatusCode.OK,
        $$"""
          {"outgoingEmail":"{{outgoingEmail:D}}","account":"work","stage":"{{stage}}","origin":"Command",
           "requester":"tool:send_email:1","attemptCount":1,"mimeByteLength":4096,
           "recordedAt":"2026-08-19T09:00:00+00:00","stageChangedAt":"2026-08-19T09:05:00+00:00",
           "availableAt":"2026-08-19T09:05:00+00:00","lastFailureCode":27001,"lastReplyCode":null,
           "recipients":[{"address":"{{recipient}}","role":"To","status":"Pending","lastReplyCode":null,
                          "answeredAt":null}]}
          """);

    /// <summary>Writes the body a decision answers with.</summary>
    /// <param name="outgoingEmail">The send the decision named.</param>
    /// <param name="outcome">What the deployment reports happened.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Decision(Guid outgoingEmail, string outcome) => (
        HttpStatusCode.OK,
        $$"""{"outgoingEmail":"{{outgoingEmail:D}}","outcome":"{{outcome}}"}""");

    /// <summary>Reports how often the command asked the deployment for a decision on the named route.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="path">The administrative route the decision is asked for on.</param>
    /// <returns>The number of requests it sent there.</returns>
    internal static int OutboxDecisionRequestCount(this FakeHttpMessageHandler deployment, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == path);
    }

    /// <summary>Reports the body the command sent to the named decision route.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="path">The administrative route the decision is asked for on.</param>
    /// <returns>The request body as it was written, or <see langword="null" /> where nothing was sent there.</returns>
    internal static string? LastDecisionBody(this FakeHttpMessageHandler deployment, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(recorded => recorded.Method == HttpMethod.Post
                && recorded.RequestUri?.AbsolutePath == path)?
            .ContentAsUtf8String();
    }

    /// <summary>Reports the query string the command read the outbox with.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query string, or <see langword="null" /> where nothing was read.</returns>
    internal static string? LastOutboxQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.OutboxPath)?
            .RequestUri?.Query;
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        (HttpStatusCode Status, string Body)? summary,
        (HttpStatusCode Status, string Body)? listing,
        (HttpStatusCode Status, string Body)? send,
        (HttpStatusCode Status, string Body)? cancellation,
        (HttpStatusCode Status, string Body)? requeue)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.OutboxSummaryPath)
        {
            return Json(summary ?? Summary(outstandingCount: 0));
        }

        if (path == AdminEndpointRoutes.OutboxCancellationPath)
        {
            return Json(cancellation ?? Decision(Guid.Empty, "Accepted"));
        }

        if (path == AdminEndpointRoutes.OutboxRequeuePath)
        {
            return Json(requeue ?? Decision(Guid.Empty, "Accepted"));
        }

        if (path == AdminEndpointRoutes.OutboxPath)
        {
            return Json(listing ?? Page());
        }

        // Everything left beneath the prefix is the single-send route, which takes the identifier as its last segment.
        if (path is { Length: > 0 } addressed
            && addressed.StartsWith($"{AdminEndpointRoutes.OutboxPath}/", StringComparison.Ordinal))
        {
            return Json(send ?? (HttpStatusCode.NotFound, string.Empty));
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Json((HttpStatusCode Status, string Body) answer) =>
        FakeAdminEndpoint.Json(answer.Status, answer.Body);
}
