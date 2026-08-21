// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the mailbox synchronization route, as the command meets one without a server.</summary>
/// <remarks>
/// The session route is answered whatever the test asked for, because every command reads it before its own operation:
/// that is where the two versions are settled, and a double serving only the status route would report a deployment
/// nothing can be administered on.
/// </remarks>
internal static class FakeMailboxDeployment
{
    /// <summary>Builds a deployment answering the synchronization status route with the body the caller supplies.</summary>
    /// <param name="status">The body the status route answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Answering(string status) =>
        new((request, _) => Task.FromResult(Answer(request, status)));

    private static HttpResponseMessage Answer(HttpRequestMessage request, string status) =>
        FakeAdminEndpoint.AnswerSession(request)
        ?? (request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxSynchronizationPath
            ? FakeAdminEndpoint.Json(HttpStatusCode.OK, status)
            : FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty));
}
