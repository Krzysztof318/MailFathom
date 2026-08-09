// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the embedding routes, as the command meets one without a server.</summary>
/// <remarks>
/// Routed by path and method rather than scripted in order, because the activation command sends two requests and what
/// is worth asserting is that the second one was not sent at all when the operator declined. A script answering in
/// sequence would hand the assessment's answer to whatever arrived first and hide exactly that.
/// </remarks>
internal static class FakeEmbeddingDeployment
{
    /// <summary>Builds a deployment that answers each embedding route with a body the caller supplies.</summary>
    /// <param name="status">The body the status route answers with.</param>
    /// <param name="assessment">The body the activation route answers a read with.</param>
    /// <param name="activation">The body the activation route answers a write with, or a refusal.</param>
    /// <param name="cancellation">The body the reindex cancellation route answers with.</param>
    /// <param name="version">The version it reports, which defaults to the one the command was built as.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// The session route is answered whatever the test asked for, because every command reads it before its own
    /// operation: that is where the two versions are settled, and a double that served the embedding routes alone
    /// would report a deployment nothing can be administered on.
    /// </remarks>
    internal static FakeHttpMessageHandler Answering(
        string? status = null,
        string? assessment = null,
        (HttpStatusCode Status, string Body)? activation = null,
        string? cancellation = null,
        string? version = null) =>
        new((request, _) => Task.FromResult(
            Answer(request, status, assessment, activation, cancellation, version ?? FakeAdminEndpoint.CommandVersion)));

    /// <summary>Reports whether the command asked the deployment to activate anything.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns><see langword="true" /> when an activation was requested.</returns>
    /// <remarks>The assertion every confirmation test is really about: a declined prompt has to leave the provider bill unstarted, not merely unreported.</remarks>
    internal static bool WasAskedToActivate(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Any(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.EmbeddingActivationPath);
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string? status,
        string? assessment,
        (HttpStatusCode Status, string Body)? activation,
        string? cancellation,
        string version)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.SessionPath)
        {
            return Json(HttpStatusCode.OK, FakeAdminEndpoint.SessionBody("workstation", version));
        }

        if (path == AdminEndpointRoutes.EmbeddingStatusPath && status is { } statusBody)
        {
            return Json(HttpStatusCode.OK, statusBody);
        }

        if (path == AdminEndpointRoutes.EmbeddingActivationPath && request.Method == HttpMethod.Get
            && assessment is { } assessmentBody)
        {
            return Json(HttpStatusCode.OK, assessmentBody);
        }

        if (path == AdminEndpointRoutes.EmbeddingActivationPath && request.Method == HttpMethod.Post
            && activation is { } answered)
        {
            return Json(answered.Status, answered.Body);
        }

        if (path == AdminEndpointRoutes.EmbeddingReindexCancellationPath && cancellation is { } cancellationBody)
        {
            return Json(HttpStatusCode.OK, cancellationBody);
        }

        return Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
