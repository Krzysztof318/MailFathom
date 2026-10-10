// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text.Json;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment answering the role, group, role-assignment, and user-permission routes.</summary>
/// <remarks>
/// Every command reads one listing at most, so one body answers every reading; a recording is answered with the
/// identifier it minted, and every other write is accepted with no body, because acceptance is the whole of what those
/// routes answer with.
/// </remarks>
internal static class FakeGrantDeployment
{
    /// <summary>The cursor the first page of a listing spread over two pages names the second by.</summary>
    private const string SecondPageCursor = "second-page";

    /// <summary>Gets the identifier this deployment reports for whatever it has just recorded.</summary>
    internal static Guid RecordedId { get; } = new("66666666-6666-6666-6666-666666666666");

    /// <summary>Builds a deployment that answers every reading with one body and accepts every write.</summary>
    /// <param name="reading">What a reading answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Answering(string reading = "{}") =>
        new((request, _) => Task.FromResult(Answer(request, reading)));

    /// <summary>Builds a deployment whose reading answers on two pages, the first naming the cursor of the second.</summary>
    /// <param name="firstPage">What the first page answers with, given the cursor it names the second by.</param>
    /// <param name="secondPage">What the second page answers with.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler AnsweringOnTwoPages(Func<string, string> firstPage, string secondPage) =>
        new((request, _) => Task.FromResult(Answer(
            request,
            request.RequestUri?.Query == $"?cursor={SecondPageCursor}" ? secondPage : firstPage(SecondPageCursor))));

    /// <summary>Builds a deployment that refuses every operation with a problem document.</summary>
    /// <param name="status">The status it refuses with.</param>
    /// <param name="detail">What the problem document says.</param>
    /// <returns>The deployment.</returns>
    internal static FakeHttpMessageHandler Refusing(HttpStatusCode status, string detail) =>
        new((request, _) => Task.FromResult(
            FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(
                status,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"status":{{(int)status}},"detail":{{JsonSerializer.Serialize(detail)}}}"""))));

    /// <summary>Writes one role as a listing carries it.</summary>
    /// <param name="id">The identifier the deployment gave the role.</param>
    /// <param name="name">The name an operator reads it by.</param>
    /// <param name="permissions">The published names it grants.</param>
    /// <returns>The role, as an element of the listing's array.</returns>
    internal static string Role(Guid id, string name, params string[] permissions) =>
        RoleListing(id, name, permissions, []);

    /// <summary>Writes one role that also stores names the build does not publish.</summary>
    /// <param name="id">The identifier the deployment gave the role.</param>
    /// <param name="name">The name an operator reads it by.</param>
    /// <param name="permissions">The published names it grants.</param>
    /// <param name="unpublished">The stored names that grant nothing.</param>
    /// <returns>The role, as an element of the listing's array.</returns>
    internal static string RoleListing(Guid id, string name, string[] permissions, string[] unpublished) =>
        $$"""{"id":"{{id:D}}","name":{{JsonSerializer.Serialize(name)}},"permissions":{{JsonSerializer.Serialize(permissions)}},"unpublished":{{JsonSerializer.Serialize(unpublished)}},"createdAt":"2026-08-20T09:00:00+00:00"}""";

    /// <summary>Writes one page of roles.</summary>
    /// <param name="nextCursor">The cursor of the following page, or <see langword="null" /> on the last.</param>
    /// <param name="roles">The roles, each written by <see cref="Role" />.</param>
    /// <returns>The page.</returns>
    internal static string RolePage(string? nextCursor, params string[] roles) =>
        $$"""{"roles":[{{string.Join(',', roles)}}],"nextCursor":{{JsonSerializer.Serialize(nextCursor)}}}""";

    /// <summary>Writes one group as a listing carries it.</summary>
    /// <param name="id">The identifier the deployment gave the group.</param>
    /// <param name="name">The name an operator reads it by.</param>
    /// <param name="organization">The organization it belongs to, or <see langword="null" /> for none.</param>
    /// <param name="members">How many users belong to it.</param>
    /// <returns>The group, as an element of the listing's array.</returns>
    internal static string Group(Guid id, string name, Guid? organization, int members) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"id":"{{id:D}}","name":{{JsonSerializer.Serialize(name)}},"organizationId":{{(organization is { } named ? $"\"{named:D}\"" : "null")}},"members":{{members}},"createdAt":"2026-08-20T09:00:00+00:00"}""");

    /// <summary>Writes one role assignment as a listing carries it.</summary>
    /// <param name="id">The identifier it is revoked by.</param>
    /// <param name="role">The role it gives.</param>
    /// <param name="principal">Who it gives the role to, as a kind and an identifier.</param>
    /// <param name="scope">What it reaches, as a kind and an identifier that is absent for the deployment.</param>
    /// <returns>The assignment, as an element of the listing's array.</returns>
    internal static string Assignment(Guid id, Guid role, (string Kind, Guid Id) principal, (string Kind, Guid? Id) scope) =>
        $$"""{"id":"{{id:D}}","roleId":"{{role:D}}","principal":{{Reference(principal.Kind, principal.Id)}},"scope":{{Reference(scope.Kind, scope.Id)}},"assignedAt":"2026-08-20T09:00:00+00:00"}""";

    /// <summary>Writes one reason a user holds a permission, as the explanation carries it.</summary>
    /// <param name="permission">The published permission name.</param>
    /// <param name="role">The role's name.</param>
    /// <param name="assignment">The assignment giving the role.</param>
    /// <param name="group">The group's name where the assignment reaches the user through one, or <see langword="null" />.</param>
    /// <param name="scope">Where the permission is held.</param>
    /// <param name="inert">Whether it reaches nothing there.</param>
    /// <param name="pattern">The pattern on the role's list that reaches the permission, or <see langword="null" /> where the role lists it by name.</param>
    /// <returns>The source, as an element of the explanation's array.</returns>
    internal static string Source(
        string permission,
        string role,
        Guid assignment,
        string? group,
        (string Kind, Guid? Id) scope,
        bool inert = false,
        string? pattern = null) =>
        $$"""{"permission":"{{permission}}","pattern":{{JsonSerializer.Serialize(pattern)}},"roleId":"{{Guid.Empty:D}}","role":{{JsonSerializer.Serialize(role)}},"assignmentId":"{{assignment:D}}","groupId":{{(group is null ? "null" : "\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\"")}},"group":{{JsonSerializer.Serialize(group)}},"scope":{{Reference(scope.Kind, scope.Id)}},"inert":{{(inert ? "true" : "false")}}}""";

    private static string Reference(string kind, Guid? id) =>
        $$"""{"kind":"{{kind}}","id":{{(id is { } named ? $"\"{named:D}\"" : "null")}}}""";

    private static HttpResponseMessage Answer(HttpRequestMessage request, string reading) =>
        FakeAdminEndpoint.AnswerSession(request) ?? AnswerOperation(request, reading);

    private static HttpResponseMessage AnswerOperation(HttpRequestMessage request, string reading)
    {
        if (request.Method == HttpMethod.Get)
        {
            return FakeAdminEndpoint.Json(HttpStatusCode.OK, reading);
        }

        return request.Method == HttpMethod.Post
            ? FakeAdminEndpoint.Json(HttpStatusCode.OK, $$"""{"id":"{{RecordedId:D}}"}""")
            : FakeAdminEndpoint.Json(HttpStatusCode.NoContent, string.Empty);
    }
}
