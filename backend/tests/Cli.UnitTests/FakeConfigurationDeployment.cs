// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the configuration routes, as the configuration commands meet one without a server.</summary>
/// <remarks>
/// <para>
/// Every command here reads before it writes, because a change is composed over the version the reading carried. The
/// double therefore answers both halves of each route and records the write, which is what lets a test assert the one
/// thing these commands genuinely decide: what was sent, and over which version.
/// </para>
/// <para>
/// The document route answers a sequence rather than a single body, because an editing session refused for a version
/// somebody else moved past reads the document a second time to report what moved. One body would make the two reads
/// indistinguishable, which is exactly the case worth covering.
/// </para>
/// </remarks>
internal static class FakeConfigurationDeployment
{
    /// <summary>Builds a deployment answering the configuration routes with the bodies given.</summary>
    /// <param name="reading">What a reading of the settings answers, or <see langword="null" /> for one setting a file supplies.</param>
    /// <param name="write">What a write answers, or <see langword="null" /> for a commit that changed one setting.</param>
    /// <param name="documents">What each successive read of the document answers, the last one repeating; or <see langword="null" /> for an empty document.</param>
    /// <param name="adoptable">What an adoption preview answers, or <see langword="null" /> for the reading above.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding(
        string? reading = null,
        string? write = null,
        IReadOnlyList<string>? documents = null,
        string? adoptable = null)
    {
        var served = documents ?? [Document(version: 1, "{}")];
        var documentReads = 0;

        return new((request, _) =>
        {
            var document = ReadsTheDocument(request)
                ? served[Math.Min(documentReads++, served.Count - 1)]
                : served[0];

            return Task.FromResult(Answer(request, reading, write, document, adoptable));
        });
    }

    /// <summary>Writes the body a reading of the settings answers with.</summary>
    /// <param name="version">The persisted version the deployment composed its settings over.</param>
    /// <param name="settings">The settings it reports, each as path, value, source, origin, and whether it is redacted.</param>
    /// <returns>The response body.</returns>
    internal static string Reading(long version, params (string Path, string Value, string Source, string? Origin, bool Redacted)[] settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reported = string.Join(',', settings.Select(setting => Setting(setting)));

        return string.Create(CultureInfo.InvariantCulture, $$"""{"version":{{version}},"settings":[{{reported}}]}""");
    }

    /// <summary>Writes the body a reading that matched nothing answers with.</summary>
    /// <param name="version">The persisted version the deployment composed its settings over.</param>
    /// <returns>The response body.</returns>
    internal static string NoSettings(long version) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"version":{{version}},"settings":[]}""");

    /// <summary>Writes the body a committed write answers with.</summary>
    /// <param name="version">The version the commit produced.</param>
    /// <param name="path">The setting the write named.</param>
    /// <param name="before">What the setting read as before, or <see langword="null" /> where no source supplied it.</param>
    /// <param name="after">What it reads as now, or <see langword="null" /> where nothing supplies it.</param>
    /// <returns>The response body.</returns>
    internal static string Committed(
        long version,
        string path = "MailboxSearch:SnippetsPerEmail",
        (string Value, string Source)? before = null,
        (string Value, string Source)? after = null)
    {
        var written = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"path":"{{path}}","before":{{Reading(path, before)}},"after":{{Reading(path, after)}}}""");

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"committed":true,"version":{{version}},"code":null,"messages":[],"changes":[{{written}}]}""");
    }

    /// <summary>Writes the body a refused write answers with.</summary>
    /// <param name="code">The five-digit code naming why it was refused.</param>
    /// <param name="version">The version in force, which the next attempt is composed over.</param>
    /// <param name="messages">What the deployment said about the refusal.</param>
    /// <returns>The response body.</returns>
    internal static string Refused(int code, long version, params string[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var stated = string.Join(',', messages.Select(message => $"\"{message}\""));

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"committed":false,"version":{{version}},"code":{{code}},"messages":[{{stated}}],"changes":[]}""");
    }

    /// <summary>Writes the body a write that changed nothing answers with, which names no code.</summary>
    /// <param name="version">The version in force, unchanged.</param>
    /// <param name="message">What the deployment said about there being nothing to do.</param>
    /// <returns>The response body.</returns>
    internal static string ChangedNothing(long version, string message) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"committed":false,"version":{{version}},"code":null,"messages":["{{message}}"],"changes":[]}""");

    /// <summary>Writes the body a read of the persisted document answers with.</summary>
    /// <param name="version">The version the document was read at.</param>
    /// <param name="document">The document itself, as the sparse JSON an editing session opens.</param>
    /// <returns>The response body.</returns>
    internal static string Document(long version, string document) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"version":{{version}},"document":{{Quoted(document)}}}""");

    /// <summary>Reports the body of the write the command last sent.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The request body, or <see langword="null" /> where no write was sent.</returns>
    internal static string? LastConfigurationWrite(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.Method == HttpMethod.Post)?
            .ContentAsUtf8String();
    }

    /// <summary>Reports how many writes the command sent, which is what says a refusal wrote nothing.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The number of writes.</returns>
    internal static int ConfigurationWriteCount(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request => request.Method == HttpMethod.Post);
    }

    /// <summary>Reports the query the command last read the settings with.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query string, or <see langword="null" /> where no reading was asked for.</returns>
    internal static string? LastReadingQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request =>
                request.Method == HttpMethod.Get
                && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.ConfigurationPath)?
            .RequestUri?.Query;
    }

    private static string Setting((string Path, string Value, string Source, string? Origin, bool Redacted) setting) =>
        $$"""
          {"path":"{{setting.Path}}","value":"{{setting.Value}}","source":"{{setting.Source}}","origin":{{(setting.Origin is null ? "null" : $"\"{setting.Origin}\"")}},"redacted":{{(setting.Redacted ? "true" : "false")}}}
          """;

    private static string Reading(string path, (string Value, string Source)? reading) => reading is { } stated
        ? $$"""{"path":"{{path}}","value":"{{stated.Value}}","source":"{{stated.Source}}","origin":null,"redacted":false}"""
        : "null";

    /// <summary>Writes a document as a JSON string value, escaping what a JSON string may not carry raw.</summary>
    private static string Quoted(string document) => JsonSerializer.Serialize(document);

    /// <summary>Reports whether the request is the one whose answer advances through the scripted documents.</summary>
    private static bool ReadsTheDocument(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.ConfigurationDocumentPath;

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string? reading,
        string? write,
        string document,
        string? adoptable)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.ConfigurationDocumentPath)
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, document)
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, write ?? Committed(version: 2));
        }

        if (path == AdminEndpointRoutes.ConfigurationAdoptionPath)
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, adoptable ?? reading ?? NoSettings(version: 1))
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, write ?? Committed(version: 2));
        }

        if (path == AdminEndpointRoutes.ConfigurationPath)
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, reading ?? NoSettings(version: 1))
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, write ?? Committed(version: 2));
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, """{"title":"Not Found"}""");
    }
}
