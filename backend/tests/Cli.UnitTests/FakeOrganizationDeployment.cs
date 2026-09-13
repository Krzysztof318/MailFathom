// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the organization routes, the user roster, and the route moving a user between organizations.</summary>
/// <remarks>
/// The roster is answered because moving a user settles which user it acts for before anything else, and every write
/// is accepted with no body, because acceptance is the whole of what those routes answer with.
/// </remarks>
internal static class FakeOrganizationDeployment
{
    /// <summary>Gets the identifier this deployment reports for an organization it has just recorded.</summary>
    internal static Guid ProvisionedOrganizationId { get; } = new("66666666-6666-6666-6666-666666666666");

    /// <summary>Builds a deployment holding the users and the organizations stated.</summary>
    /// <param name="users">The users the roster reports, in the order it serves them.</param>
    /// <param name="organizations">What a listing of the organizations answers with, each written by <see cref="Organization" />.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Holding(IReadOnlyList<Guid> users, params string[] organizations) =>
        new((request, _) => Task.FromResult(Answer(request, users, organizations)));

    /// <summary>Writes one organization as a listing carries it.</summary>
    /// <param name="id">The identifier the deployment gave the organization.</param>
    /// <param name="shortName">The short name its members sign in under.</param>
    /// <param name="displayName">The name an operator reads it by.</param>
    /// <param name="members">How many users belong to it.</param>
    /// <returns>The organization, as an element of the listing's array.</returns>
    internal static string Organization(Guid id, string shortName, string displayName, int members) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{id:D}}","displayName":"{{displayName}}","shortName":"{{shortName}}","members":{{members}},"createdAt":"2026-08-20T09:00:00+00:00"}""");

    private static HttpResponseMessage Answer(
        HttpRequestMessage request,
        IReadOnlyList<Guid> users,
        IReadOnlyList<string> organizations)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path == AdminEndpointRoutes.UsersPath)
        {
            return FakeAdminEndpoint.Json(
                HttpStatusCode.OK,
                $$"""{"users":[{{string.Join(',', users.Select(Roster))}}]}""");
        }

        if (path == AdminEndpointRoutes.OrganizationsPath)
        {
            return request.Method == HttpMethod.Get
                ? FakeAdminEndpoint.Json(HttpStatusCode.OK, $$"""{"organizations":[{{string.Join(',', organizations)}}]}""")
                : FakeAdminEndpoint.Json(HttpStatusCode.OK, $$"""{"organizationId":"{{ProvisionedOrganizationId:D}}"}""");
        }

        if (path.StartsWith($"{AdminEndpointRoutes.OrganizationsPath}/", StringComparison.Ordinal)
            || path.EndsWith("/organization", StringComparison.Ordinal))
        {
            // Acceptance is the whole answer, so the deployment sends no body and the command has nothing to read.
            return FakeAdminEndpoint.Json(HttpStatusCode.NoContent, string.Empty);
        }

        return FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(HttpStatusCode.NotFound, string.Empty);
    }

    private static string Roster(Guid user) =>
        $$"""{"id":"{{user:D}}","displayName":"user-{{user:D}}","served":true}""";
}
