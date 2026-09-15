// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the six export routes, from the measurement to the archive itself.</summary>
/// <remarks>
/// <para>
/// One double answering every route rather than one per command, because the commands are about the sequence: starting
/// an export measures first and then asks at a second path, and a double serving only the write would let a command
/// that skipped the measurement pass unnoticed.
/// </para>
/// <para>
/// The archive is answered as bytes rather than as JSON, because the download is the one route that is not a document —
/// a double serving it as JSON would let a command that buffered a whole mailbox into memory pass.
/// </para>
/// </remarks>
internal static class FakeExportDeployment
{
    /// <summary>The export every scenario here names, which is what the commands are pointed at.</summary>
    internal static readonly Guid ExportId = new("0199a0c0-0000-7000-8000-000000000006");

    private const string RequestedAt = "2026-09-15T11:00:00+00:00";

    private const string CompletedAt = "2026-09-15T11:20:00+00:00";

    private const string ExpiresAt = "2026-09-17T11:20:00+00:00";

    /// <summary>Builds a deployment answering every export route.</summary>
    /// <param name="measurement">What measuring answers with.</param>
    /// <param name="start">What asking for an export answers with.</param>
    /// <param name="export">What reading, cancelling, or deleting one answers with.</param>
    /// <param name="listing">What listing the account's exports answers with.</param>
    /// <param name="archive">What the archive route serves.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Exporting(
        string? measurement = null,
        string? start = null,
        string? export = null,
        string? listing = null,
        byte[]? archive = null)
    {
        var measured = measurement ?? Measurement();
        var started = start ?? Start();
        var one = export ?? Export();
        var listed = listing ?? Listing(one);
        var served = archive ?? Archive();

        return new FakeHttpMessageHandler((request, _) =>
            Task.FromResult(Answer(request, measured, started, one, listed, served)));
    }

    /// <summary>Builds a deployment that holds no such export, which is what every route answers on a wrong identity.</summary>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler WithNoSuchExport() =>
        new((request, _) => Task.FromResult(
            FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty)));

    /// <summary>Writes the body a measurement answers with.</summary>
    /// <param name="messageCount">How many messages the export would carry.</param>
    /// <param name="byteCount">How many bytes of stored mail they hold between them.</param>
    /// <param name="folders">The per-folder figures, or nothing where the answer carries none.</param>
    /// <returns>The response body.</returns>
    internal static string Measurement(
        long messageCount = 1_240,
        long byteCount = 8_388_608,
        string? folders = null) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"messageCount":{{messageCount}},"byteCount":{{byteCount}},"folders":{{folders ?? "null"}}}""");

    /// <summary>Writes one folder's contribution, as the measurement carries it.</summary>
    /// <param name="path">The deployment's own local path for the folder.</param>
    /// <param name="messageCount">How many messages it holds.</param>
    /// <param name="byteCount">How many bytes of stored mail they hold.</param>
    /// <returns>The folder, as the measurement's array holds it.</returns>
    internal static string Folder(string path, long messageCount, long byteCount) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"path":"{{path}}","messageCount":{{messageCount}},"byteCount":{{byteCount}}}""");

    /// <summary>Writes the body starting an export answers with.</summary>
    /// <param name="export">The export it produced, or nothing where the deployment answered without one.</param>
    /// <param name="wasAlreadyRunning">Whether the answer is an export that was already being written.</param>
    /// <returns>The response body.</returns>
    internal static string Start(string? export = null, bool wasAlreadyRunning = false) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"measurement":{{Measurement()}},"export":{{export ?? Export(state: "Queued")}},"wasAlreadyRunning":{{(wasAlreadyRunning ? "true" : "false")}}}""");

    /// <summary>Writes one export, as every route that carries one does.</summary>
    /// <param name="state">The deployment's own word for what the export is doing.</param>
    /// <param name="folder">The one folder it covers, or nothing for a whole mailbox.</param>
    /// <param name="messageCount">How many messages it has written.</param>
    /// <param name="byteCount">How many bytes of stored mail those messages hold.</param>
    /// <param name="finished">Whether the archive exists, which is what gives it a length and an expiry.</param>
    /// <param name="failureCode">The stable code of what stopped it, or nothing where nothing did.</param>
    /// <returns>The export, as a body or as an element of the listing.</returns>
    internal static string Export(
        string state = "Completed",
        string? folder = null,
        long messageCount = 1_240,
        long byteCount = 8_388_608,
        bool finished = true,
        int? failureCode = null) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {"id":"{{ExportId:D}}","account":"work","folder":{{(folder is null ? "null" : $"\"{folder}\"")}},"state":"{{state}}","requestedAt":"{{RequestedAt}}","messageCount":{{messageCount}},"byteCount":{{byteCount}},"archiveByteLength":{{(finished ? "4194304" : "null")}},"completedAt":{{(finished ? $"\"{CompletedAt}\"" : "null")}},"expiresAt":{{(finished ? $"\"{ExpiresAt}\"" : "null")}},"failureCode":{{failureCode?.ToString(CultureInfo.InvariantCulture) ?? "null"}}}
        """);

    /// <summary>Writes the body listing the account's exports answers with.</summary>
    /// <param name="exports">The exports, newest first.</param>
    /// <returns>The response body.</returns>
    internal static string Listing(params string[] exports) =>
        $$"""{"exports":[{{string.Join(',', exports)}}]}""";

    /// <summary>Writes the bytes the archive route serves, which are a zip's own rather than a document.</summary>
    /// <returns>The archive.</returns>
    internal static byte[] Archive() =>
        [.. "PK\u0003\u0004"u8.ToArray(), .. Encoding.ASCII.GetBytes(new string('m', 4_096))];

    /// <summary>Reports how often the command asked the deployment to start an export.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of requests that asked for one.</returns>
    internal static int StartRequestCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == HttpMethod.Post
            && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxExportsPath);
    }

    /// <summary>Reports how often the command sent a request of one verb to one path.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="method">The verb.</param>
    /// <param name="path">The route.</param>
    /// <returns>The number of requests it sent there.</returns>
    internal static int RequestCount(this FakeHttpMessageHandler deployment, HttpMethod method, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == method && request.RequestUri?.AbsolutePath == path);
    }

    /// <summary>Reports the query the command sent to one path, which is where the account and the folder travel.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="path">The route.</param>
    /// <returns>The query string, or an empty string where the command sent none.</returns>
    internal static string QuerySentTo(this FakeHttpMessageHandler deployment, string path)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .Where(request => request.RequestUri?.AbsolutePath == path)
            .Select(request => request.RequestUri?.Query ?? string.Empty)
            .LastOrDefault() ?? string.Empty;
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string measurement,
        string start,
        string export,
        string listing,
        byte[] archive)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.MailboxExportMeasurementPath)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, measurement);
        }

        if (path == AdminEndpointRoutes.MailboxExportsPath)
        {
            return FakeAdminEndpoint.Json(
                HttpStatusCode.OK,
                request.Method == HttpMethod.Get ? listing : start);
        }

        if (path == AdminEndpointRoutes.MailboxExportArchivePath(ExportId))
        {
            return Zip(archive);
        }

        if (path == AdminEndpointRoutes.MailboxExportCancellationPath(ExportId)
            || path == AdminEndpointRoutes.MailboxExportPath(ExportId))
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, export);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Zip(byte[] archive) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(archive)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/zip") },
        },
    };
}
