// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the folder routes, as the erase command meets one without a server.</summary>
/// <remarks>
/// The erasure route is answered from a queue rather than with one body, because what the command is about is the
/// repetition: a folder larger than one pass is erased by asking again until nothing is left, and a double answering
/// the same body every time would either never end or hide that the command asked more than once.
/// </remarks>
internal static class FakeFolderDeployment
{
    /// <summary>Builds a deployment that answers each erasure request with the next pass the caller scripted.</summary>
    /// <param name="passes">What the erasure route answers, in order; the last is repeated once the script runs out.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// The session route is answered unconditionally, because every command settles the two versions there before its
    /// own operation and a double serving the erasure route alone would report a deployment nothing can be
    /// administered on.
    /// </remarks>
    internal static FakeHttpMessageHandler Erasing(params (HttpStatusCode Status, string Body)[] passes)
    {
        var remaining = new Queue<(HttpStatusCode Status, string Body)>(passes);

        return new FakeHttpMessageHandler((request, _) => Task.FromResult(Answer(request, remaining, passes)));
    }

    /// <summary>Writes the body a completed pass answers with.</summary>
    /// <param name="erasedEmailCount">How many stored emails the pass removed.</param>
    /// <param name="emailsRemain">Whether the folder still holds mail a further pass would reach.</param>
    /// <returns>The response body.</returns>
    internal static (HttpStatusCode Status, string Body) Pass(int erasedEmailCount, bool emailsRemain) => (
        HttpStatusCode.OK,
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              {"account":"work","folder":"ARCHIVE","erasedEmailCount":{{erasedEmailCount}},"emailsRemain":{{(emailsRemain ? "true" : "false")}}}
              """));

    /// <summary>Reports how often the command asked the deployment to erase a pass.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of erasure requests it sent.</returns>
    internal static int ErasureRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.FolderErasurePath);
    }

    /// <summary>Reports the body of the erasure the command last asked for.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The request body, or <see langword="null" /> where no erasure was asked for.</returns>
    internal static string? LastErasureRequest(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.FolderErasurePath)?
            .ContentAsUtf8String();
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        Queue<(HttpStatusCode Status, string Body)> remaining,
        (HttpStatusCode Status, string Body)[] passes)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.SessionPath)
        {
            return Json(
                HttpStatusCode.OK,
                FakeAdminEndpoint.SessionBody("workstation", FakeAdminEndpoint.CommandVersion));
        }

        if (path == AdminEndpointRoutes.FolderErasurePath && passes.Length > 0)
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
