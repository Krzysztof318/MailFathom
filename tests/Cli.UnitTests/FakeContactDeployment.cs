// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the contact routes, as the contact commands meet one without a server.</summary>
/// <remarks>
/// Each route is answered from a body the caller scripted, because what these commands are about is what they send and
/// what they print: an amendment is a read followed by a write, so the double has to answer both and record the second
/// for the test to read.
/// </remarks>
internal static class FakeContactDeployment
{
    /// <summary>The identity every scripted contact carries, so a test names one contact in one place.</summary>
    internal static readonly Guid ContactIdentity = new("11111111-2222-3333-4444-555555555555");

    /// <summary>Builds a deployment answering the contact routes with the bodies given.</summary>
    /// <param name="lookup">What a read of one contact answers, or <see langword="null" /> to answer that the book holds none.</param>
    /// <param name="write">What a write answers, or <see langword="null" /> where the test asks for none.</param>
    /// <param name="page">What a listing answers, or <see langword="null" /> where the test asks for none.</param>
    /// <param name="erasure">What an erasure answers, or <see langword="null" /> where the test asks for none.</param>
    /// <param name="export">What an export answers, or <see langword="null" /> where the test asks for none.</param>
    /// <param name="version">The release this deployment reports, or <see langword="null" /> for the command's own.</param>
    /// <returns>The deployment.</returns>
    /// <remarks>
    /// The session route is answered unconditionally, because every command settles the two versions there before its
    /// own operation and a double serving the contact routes alone would report a deployment nothing can be
    /// administered on. That settling is also the one thing a contact command does before its own work, which is why
    /// the version is stated here at all: a test about what a command writes before it refuses has no other way to make
    /// it write anything.
    /// </remarks>
    internal static FakeHttpMessageHandler Holding(
        string? lookup = null,
        string? write = null,
        string? page = null,
        string? erasure = null,
        string? export = null,
        string? version = null) =>
        new((request, _) => Task.FromResult(Answer(request, lookup, write, page, erasure, export, version)));

    /// <summary>Writes the body a read of one contact answers with.</summary>
    /// <param name="displayName">The name the contact carries.</param>
    /// <param name="addresses">The addresses it holds, the preferred one first.</param>
    /// <param name="origin">How the contact came to be in the book.</param>
    /// <param name="note">What the owner wrote about the person, or <see langword="null" /> for none.</param>
    /// <returns>The response body.</returns>
    internal static string Lookup(
        string displayName = "Anna Kowalska",
        string[]? addresses = null,
        string origin = "Asserted",
        string? note = null) =>
        $$"""{"contact":{{Contact(displayName, addresses, origin, note)}}}""";

    /// <summary>Writes the body a write answers with when the book performed it.</summary>
    /// <param name="displayName">The name the written record carries.</param>
    /// <param name="addresses">The addresses it holds, the preferred one first.</param>
    /// <param name="origin">How the contact came to be in the book.</param>
    /// <returns>The response body.</returns>
    internal static string Written(
        string displayName = "Anna Kowalska",
        string[]? addresses = null,
        string origin = "Asserted") =>
        $$"""{"outcome":"Written","contact":{{Contact(displayName, addresses, origin, note: null)}}}""";

    /// <summary>Writes the body a promotion answers with, which names the outcome and carries no record.</summary>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// The deployment's answer to a write whose caller stated no record: writing the book and reading it are different
    /// permissions, so a promotion is told that it happened rather than handed the person it happened to.
    /// </remarks>
    internal static string Promoted() => """{"outcome":"Written","contact":null,"addressHolder":null}""";

    /// <summary>Writes the body a write answers with when an outcome refused it.</summary>
    /// <param name="outcome">The outcome that refused the write.</param>
    /// <param name="addressHolder">The contact holding a claimed address, where that is what refused it.</param>
    /// <returns>The response body.</returns>
    internal static string Refused(string outcome, Guid? addressHolder = null) => addressHolder is { } holder
        ? $$"""{"outcome":"{{outcome}}","contact":null,"addressHolder":"{{holder:D}}"}"""
        : $$"""{"outcome":"{{outcome}}","contact":null,"addressHolder":null}""";

    /// <summary>Writes the body a listing answers with.</summary>
    /// <param name="nextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the book.</param>
    /// <param name="names">The names the page's contacts carry, in the order they are served.</param>
    /// <returns>The response body.</returns>
    internal static string Page(string? nextCursor, params string[] names)
    {
        var contacts = string.Join(
            ',',
            names.Select((name, position) => Contact(
                name,
                [$"person{position.ToString(CultureInfo.InvariantCulture)}@example.test"],
                "Asserted",
                note: null,
                identity: new Guid(position + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]))));

        var cursor = nextCursor is null ? "null" : $"\"{nextCursor}\"";

        return $$"""{"contacts":[{{contacts}}],"nextCursor":{{cursor}}}""";
    }

    /// <summary>Writes the body an erasure answers with.</summary>
    /// <param name="wasHeld">Whether the book held the contact when the erasure ran.</param>
    /// <param name="addressesErased">How many addresses went with the person.</param>
    /// <returns>The response body.</returns>
    internal static string Erasure(bool wasHeld, int addressesErased) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"contact":"{{ContactIdentity:D}}","wasHeld":{{(wasHeld ? "true" : "false")}},"addressesErased":{{addressesErased}}}""");

    /// <summary>Writes the body an export answers with.</summary>
    /// <param name="displayName">The name the exported record carries.</param>
    /// <returns>The response body.</returns>
    internal static string Export(string displayName = "Anna Kowalska") =>
        $$"""{"contact":{{Contact(displayName, addresses: null, "Asserted", note: null)}},"producedAt":"2026-08-16T09:00:00+00:00"}""";

    /// <summary>Reports the body of the write the command last sent.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The request body, or <see langword="null" /> where no write was sent.</returns>
    internal static string? LastWriteRequest(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)?
            .ContentAsUtf8String();
    }

    /// <summary>Reports how many requests the command sent to the contact routes.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <param name="method">The verb to count.</param>
    /// <returns>The number of such requests.</returns>
    internal static int ContactRequestCount(this FakeHttpMessageHandler deployment, HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests.Count(request =>
            request.Method == method
            && request.RequestUri?.AbsolutePath.StartsWith(AdminEndpointRoutes.ContactsPath, StringComparison.Ordinal) == true);
    }

    /// <summary>Reports the query the command last listed the book with.</summary>
    /// <param name="deployment">The deployment the command was pointed at.</param>
    /// <returns>The query string, or <see langword="null" /> where no listing was asked for.</returns>
    internal static string? LastListingQuery(this FakeHttpMessageHandler deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        return deployment.RecordedRequests
            .LastOrDefault(request =>
                request.Method == HttpMethod.Get
                && request.RequestUri?.AbsolutePath == AdminEndpointRoutes.ContactsPath)?
            .RequestUri?.Query;
    }

    private static string Contact(
        string displayName,
        string[]? addresses,
        string origin,
        string? note,
        Guid? identity = null)
    {
        var held = addresses ?? ["anna@example.test"];
        var written = string.Join(',', held.Select(address => $"\"{address}\""));
        var recorded = note is null ? "null" : $"\"{note}\"";

        return $$"""
                 {"id":"{{identity ?? ContactIdentity:D}}","displayName":"{{displayName}}","addresses":[{{written}}],"preferredAddress":"{{held[0]}}","note":{{recorded}},"origin":"{{origin}}","recordedAt":"2026-08-01T10:00:00+00:00","amendedAt":"2026-08-02T11:00:00+00:00"}
                 """;
    }

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        string? lookup,
        string? write,
        string? page,
        string? erasure,
        string? export,
        string? version)
    {
        var path = request.RequestUri?.AbsolutePath;

        if (path == AdminEndpointRoutes.SessionPath)
        {
            return Json(
                HttpStatusCode.OK,
                FakeAdminEndpoint.SessionBody("workstation", version ?? FakeAdminEndpoint.CommandVersion));
        }

        if (path == AdminEndpointRoutes.ContactsPath)
        {
            return request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, page ?? Page(nextCursor: null))
                : Json(HttpStatusCode.OK, write ?? Written());
        }

        if (path == AdminEndpointRoutes.ContactByAddressPath)
        {
            return Json(HttpStatusCode.OK, lookup ?? """{"contact":null}""");
        }

        if (path?.EndsWith("/export", StringComparison.Ordinal) == true)
        {
            return Json(HttpStatusCode.OK, export ?? """{"contact":null,"producedAt":null}""");
        }

        if (path?.EndsWith("/promotion", StringComparison.Ordinal) == true)
        {
            return Json(HttpStatusCode.OK, write ?? Written());
        }

        if (path?.StartsWith(AdminEndpointRoutes.ContactsPath, StringComparison.Ordinal) == true)
        {
            if (request.Method == HttpMethod.Delete)
            {
                return Json(HttpStatusCode.OK, erasure ?? Erasure(wasHeld: true, addressesErased: 1));
            }

            return request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, lookup ?? """{"contact":null}""")
                : Json(HttpStatusCode.OK, write ?? Written());
        }

        return Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
