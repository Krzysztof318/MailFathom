// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the two maintenance routes, as the commands meet one without a server.</summary>
/// <remarks>
/// The re-derivation route is answered on both of its verbs from one double, because what the two commands are about
/// is the split between them: asking writes the run down and returns, and reading it is a second command against the
/// same path. A double serving only the write would let a command that waited for the walk pass unnoticed.
/// </remarks>
internal static class FakeMaintenanceDeployment
{
    private const string RequestedAt = "2026-08-18T12:00:00+00:00";

    private const string FinishedAt = "2026-08-18T12:41:00+00:00";

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

    /// <summary>Builds a deployment answering the re-derivation route on both of its verbs.</summary>
    /// <param name="start">What asking for a run answers with.</param>
    /// <param name="state">What reading the run answers with, or nothing when the scope has never had one.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Rederiving(string start, string? state = null)
    {
        var read = state ?? State(run: null);

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(
            AnswerRederivation(request, start, read)));
    }

    /// <summary>Writes the body asking for a re-derivation answers with.</summary>
    /// <param name="run">The run the scope now has.</param>
    /// <param name="started">Whether this request is what put it there.</param>
    /// <param name="carriage">What the deployment says is carrying the segment it is on.</param>
    /// <returns>The response body.</returns>
    internal static string Start(string run, bool started = true, string carriage = "carried") => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"started":{{Written(started)}},"carriage":"{{carriage}}","run":{{run}}}""");

    /// <summary>Writes the body reading a scope's re-derivation answers with.</summary>
    /// <param name="run">The run, or nothing where the scope has never been asked for one.</param>
    /// <returns>The response body.</returns>
    internal static string State(string? run) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"account":"work","folder":null,"run":{{run ?? "null"}}}""");

    /// <summary>Writes one run, as both of the route's verbs carry it.</summary>
    /// <param name="rederivedEmailCount">How many stored emails the run has re-read.</param>
    /// <param name="unreadableEmailCount">How many carried MIME no reader could parse.</param>
    /// <param name="missingContentEmailCount">How many no longer had raw MIME to re-read.</param>
    /// <param name="isOutstanding">Whether the run is still waiting to be carried further.</param>
    /// <returns>The run, as the body of either answer embeds it.</returns>
    internal static string Run(
        int rederivedEmailCount = 0,
        int unreadableEmailCount = 0,
        int missingContentEmailCount = 0,
        bool isOutstanding = true)
    {
        var endedAt = isOutstanding ? "null" : $"\"{FinishedAt}\"";

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"account":"work","folder":null,"requestedAt":"{{RequestedAt}}","isOutstanding":{{Written(isOutstanding)}},"rederivedEmailCount":{{rederivedEmailCount}},"unreadableEmailCount":{{unreadableEmailCount}},"missingContentEmailCount":{{missingContentEmailCount}},"endedAt":{{endedAt}}}""");
    }

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

    /// <summary>Reports how often the command asked the deployment for a re-derivation.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of re-derivation requests it sent.</returns>
    internal static int RederivationRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRederivationPath);
    }

    /// <summary>Reports the query the run was read with, which is where the scope is written for a read.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query string, or <see langword="null" /> where no run was read.</returns>
    internal static string? LastRederivationQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.Method == HttpMethod.Get
                && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRederivationPath)?
            .RequestUri?.Query;
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
        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRewindPath)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, request.Method == HttpMethod.Get ? assessment : rewind);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage AnswerRederivation(HttpRequestMessage request, string start, string state)
    {
        if (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRederivationPath)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, request.Method == HttpMethod.Get ? state : start);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static string Written(bool value) => value ? "true" : "false";
}
