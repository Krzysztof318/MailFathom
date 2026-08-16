// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the two maintenance routes, as the commands meet one without a server.</summary>
/// <remarks>
/// The re-derivation route is answered from a queue rather than with one body, because what that command is about is
/// the repetition: a scope larger than one pass is re-read by asking again until nothing is left, and a double
/// answering the same body every time would either never end or hide that the command asked more than once.
/// </remarks>
internal static class FakeMaintenanceDeployment
{
    /// <summary>Builds a deployment that reports a cost and accepts the rewind that follows.</summary>
    /// <param name="storedEmailCount">What the assessment reports the scope holds.</param>
    /// <param name="rewoundFolders">The aliases the rewind reports as having held progress.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Rewinding(int storedEmailCount, params string[] rewoundFolders)
    {
        var assessment = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"account":"work","folder":null,"storedEmailCount":{{storedEmailCount}}}""");
        var rewind = $$"""
                       {"account":"work","folder":null,"folders":[{{string.Join(
                           ",",
                           rewoundFolders.Select(folder => $"\"{folder}\""))}}]}
                       """;

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(
            AnswerRewind(request, assessment, rewind)));
    }

    /// <summary>Builds a deployment answering each re-derivation request with the next pass the caller scripted.</summary>
    /// <param name="passes">What the route answers, in order; the last is repeated once the script runs out.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Rederiving(params (HttpStatusCode Status, string Body)[] passes)
    {
        var remaining = new Queue<(HttpStatusCode Status, string Body)>(passes);

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(
            AnswerRederivation(request, remaining, passes)));
    }

    /// <summary>Writes the body a completed re-derivation pass answers with.</summary>
    /// <param name="rederivedEmailCount">How many stored emails the pass re-read.</param>
    /// <param name="emailsRemain">Whether the scope still holds mail a further pass would reach.</param>
    /// <param name="unreadableEmailCount">How many carried MIME no reader could parse.</param>
    /// <param name="missingContentEmailCount">How many no longer had raw MIME to re-read.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Pass(
        int rederivedEmailCount,
        bool emailsRemain,
        int unreadableEmailCount = 0,
        int missingContentEmailCount = 0) => (
        HttpStatusCode.OK,
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              {"account":"work","folder":null,"rederivedEmailCount":{{rederivedEmailCount}},"unreadableEmailCount":{{unreadableEmailCount}},"missingContentEmailCount":{{missingContentEmailCount}},"emailsRemain":{{(emailsRemain ? "true" : "false")}}}
              """));

    /// <summary>Reports how often the command asked the deployment to discard progress.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of rewind requests it sent.</returns>
    internal static int RewindRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRewindPath);
    }

    /// <summary>Reports how often the command asked the deployment for a re-derivation pass.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of re-derivation requests it sent.</returns>
    internal static int RederivationRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRederivationPath);
    }

    /// <summary>Reports the query the command read the cost with, which is where the scope is written for a read.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query string, or <see langword="null" /> where no cost was read.</returns>
    internal static string? LastAssessmentQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.Method == HttpMethod.Get
                && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRewindPath)?
            .RequestUri?.Query;
    }

    /// <summary>Reports the body of the last maintenance write the command asked for.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="path">The route whose request body is read.</param>
    /// <returns>The request body, or <see langword="null" /> where nothing was asked for.</returns>
    internal static string? LastRequestTo(this FakeHttpMessageHandler deployment, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == path)?
            .ContentAsUtf8String();
    }

    private static HttpResponseMessage AnswerRewind(HttpRequestMessage request, string assessment, string rewind)
    {
        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.SessionPath)
        {
            return Json(
                HttpStatusCode.OK,
                FakeAdminEndpoint.SessionBody("workstation", FakeAdminEndpoint.CommandVersion));
        }

        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRewindPath)
        {
            return Json(HttpStatusCode.OK, request.Method == HttpMethod.Get ? assessment : rewind);
        }

        return Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage AnswerRederivation(
        HttpRequestMessage request,
        Queue<(HttpStatusCode Status, string Body)> remaining,
        (HttpStatusCode Status, string Body)[] passes)
    {
        // The session route is answered unconditionally, because every command settles the two versions there before
        // its own operation and a double serving only its own route would report a deployment nothing can be
        // administered on. It is repeated rather than shared with the rewind double, because a helper returning a
        // response the caller then returns is a disposable this analyzer cannot see through.
        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.SessionPath)
        {
            return Json(
                HttpStatusCode.OK,
                FakeAdminEndpoint.SessionBody("workstation", FakeAdminEndpoint.CommandVersion));
        }

        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRederivationPath && passes.Length > 0)
        {
            var pass = remaining.Count > 0 ? remaining.Dequeue() : passes[^1];

            return Json(pass.Status, pass.Body);
        }

        return Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
