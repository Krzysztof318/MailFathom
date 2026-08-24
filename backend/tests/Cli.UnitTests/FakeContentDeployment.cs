// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the three content-move routes, as the commands meet one without a server.</summary>
/// <remarks>
/// The move path is answered on both of its verbs from one double, because what the commands are about is the split
/// between them: asking writes the move down and returns, and watching it is a second command against the same path. A
/// double serving only the write would let a command that waited for the whole mailbox to be carried pass unnoticed.
/// </remarks>
internal static class FakeContentDeployment
{
    private const string RequestedAt = "2026-08-24T12:00:00+00:00";

    private const string EndedAt = "2026-08-24T14:20:00+00:00";

    /// <summary>Builds a deployment answering every content-move route.</summary>
    /// <param name="report">What reading the move answers with.</param>
    /// <param name="run">What asking for, stopping, or resuming a move answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Moving(string report, string? run = null)
    {
        var written = run ?? Run();

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(Answer(request, report, written)));
    }

    /// <summary>Builds a deployment that has never been asked for a move and refuses to stop or resume one.</summary>
    /// <param name="report">What reading the move answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler WithNoMoveToActOn(string report) =>
        new((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath is AdminEndpointRoutes.ContentMovePausePath
                or AdminEndpointRoutes.ContentMoveResumePath
                ? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty)
                : Answer(request, report, Run())));

    /// <summary>Writes the body reading the move answers with.</summary>
    /// <param name="run">The move the deployment has, or nothing when none was ever asked for.</param>
    /// <param name="available">Whether the deployment has an object backend to move into at all.</param>
    /// <param name="remainingPayloadCount">How many payloads the database still holds.</param>
    /// <param name="remainingByteCount">How many bytes of raw MIME they carry between them.</param>
    /// <returns>The response body.</returns>
    internal static string Report(
        string? run = null,
        bool available = true,
        long remainingPayloadCount = 0,
        long remainingByteCount = 0) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"available":{{Written(available)}},"run":{{run ?? "null"}},"remainingPayloadCount":{{remainingPayloadCount}},"remainingByteCount":{{remainingByteCount}}}""");

    /// <summary>Writes one move, as every one of the three routes carries it.</summary>
    /// <param name="state">The deployment's own word for what the move is doing.</param>
    /// <param name="copiedPayloadCount">How many payloads it has carried into the bucket.</param>
    /// <param name="failedPayloadCount">How many it left in the database.</param>
    /// <param name="movedByteCount">How many bytes of raw MIME the carried payloads held.</param>
    /// <param name="ended">Whether it reached the end of the content.</param>
    /// <returns>The move, as the body of any of the three answers embeds it.</returns>
    internal static string Run(
        string state = "running",
        long copiedPayloadCount = 0,
        long failedPayloadCount = 0,
        long movedByteCount = 0,
        bool ended = false) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"state":"{{state}}","requestedAt":"{{RequestedAt}}","copiedPayloadCount":{{copiedPayloadCount}},"failedPayloadCount":{{failedPayloadCount}},"movedByteCount":{{movedByteCount}},"endedAt":{{(ended ? $"\"{EndedAt}\"" : "null")}}}""");

    /// <summary>Reports how often the command asked the deployment to start carrying its content.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of requests that asked for a move.</returns>
    internal static int MoveRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.ContentMovePath);
    }

    /// <summary>Reports how often the command asked the deployment to act on the move at one path.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="path">The route the decision is written to.</param>
    /// <returns>The number of requests it sent there.</returns>
    internal static int DecisionCount(this FakeHttpMessageHandler deployment, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == path);
    }

    private static HttpResponseMessage Answer(HttpRequestMessage request, string report, string run)
    {
        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.ContentMovePath)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, request.Method == HttpMethod.Get ? report : run);
        }

        if (request.RequestUri?.AbsolutePath is AdminEndpointRoutes.ContentMovePausePath
            or AdminEndpointRoutes.ContentMoveResumePath)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, run);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static string Written(bool value) => value ? "true" : "false";
}
