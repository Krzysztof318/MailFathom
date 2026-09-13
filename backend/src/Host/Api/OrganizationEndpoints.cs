// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Records, lists, renames, and removes organizations, and moves a user into or out of one.</summary>
/// <remarks>
/// <para>
/// An organization groups users and scopes a Basic username: a member signs in as <c>SHORTNAME/username</c>. So these
/// routes decide how the deployment's people are named when they sign in, and nothing about what any of them may read.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" />, recording, renaming, and removing an organization are
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, and changing a short name and moving a user are
/// <see cref="MailFathomPermission.AdminCredentialsWrite" />, for the reasons <see cref="OrganizationAdministration" />
/// gives.
/// </para>
/// </remarks>
internal static class OrganizationEndpoints
{
    /// <summary>The route organizations are listed and recorded at, relative to the administrative prefix.</summary>
    internal const string OrganizationsRoute = "/organizations";

    /// <summary>The route one organization is removed at.</summary>
    internal const string OrganizationRoute = "/organizations/{organizationId:guid}";

    /// <summary>The route one organization's display name is replaced at.</summary>
    internal const string OrganizationDisplayNameRoute = $"{OrganizationRoute}/display-name";

    /// <summary>The route one organization's short name is replaced at.</summary>
    internal const string OrganizationShortNameRoute = $"{OrganizationRoute}/short-name";

    /// <summary>The route the organization one user belongs to is set or cleared at.</summary>
    internal const string UserOrganizationRoute = "/users/{userId:guid}/organization";

    /// <summary>The greatest request body the write routes read before refusing it.</summary>
    /// <remarks>A body here is a display name, a short name, or an identifier, every one of which is bounded far below this.</remarks>
    internal const int MaxRequestBytes = 4 * 1024;

    /// <summary>Maps the organization routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOrganizations(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OrganizationsRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(OrganizationsRoute, CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPut(OrganizationDisplayNameRoute, RenameAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPut(OrganizationShortNameRoute, ChangeShortNameAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapDelete(OrganizationRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPut(UserOrganizationRoute, SetUserOrganizationAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);
    }

    /// <summary>Lists the organizations this deployment holds.</summary>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the organizations, ordered by short name.</returns>
    internal static async Task<Ok<OrganizationListResponse>> ListAsync(
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        var held = await organizations.ReadAsync(cancellationToken);

        return TypedResults.Ok(new OrganizationListResponse([.. held.Select(OrganizationResponse.For)]));
    }

    /// <summary>Records an organization.</summary>
    /// <param name="request">The display name and the short name.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the minted identifier, <c>409</c> when the short name is taken or the deployment already holds as many organizations as one may, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<OrganizationProvisionedResponse>, ProblemHttpResult>> CreateAsync(
        [FromBody] OrganizationProvisioningRequest? request,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        if (OrganizationAdministration.FindDisplayNameRefusal(request?.DisplayName) is { } refusal)
        {
            return Refused(refusal);
        }

        if (!OrganizationShortName.TryCreate(request!.ShortName, out var shortName))
        {
            return Refused(OrganizationShortName.DescribeAcceptedForm());
        }

        var result = await organizations.CreateAsync(request.DisplayName, shortName, cancellationToken);

        return result.Outcome switch
        {
            OrganizationWriteOutcome.Written => TypedResults.Ok(new OrganizationProvisionedResponse(result.OrganizationId)),
            OrganizationWriteOutcome.OrganizationCeilingReached => TypedResults.Problem(
                $"This deployment already holds the {Organization.MaximumListed} organizations one deployment may, so "
                + "no other is recorded. Remove one nobody belongs to first.",
                statusCode: StatusCodes.Status409Conflict),
            _ => ShortNameTaken(shortName),
        };
    }

    /// <summary>Replaces the name an operator reads an organization by.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="request">The new display name.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it stands, <c>404</c> when no such organization exists, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> RenameAsync(
        Guid organizationId,
        [FromBody] OrganizationDisplayNameRequest? request,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        if (OrganizationAdministration.FindDisplayNameRefusal(request?.DisplayName) is { } refusal)
        {
            return Refused(refusal);
        }

        var result = await organizations.RenameAsync(organizationId, request!.DisplayName, cancellationToken);

        return result.Outcome == OrganizationWriteOutcome.Written ? TypedResults.NoContent() : NoSuchOrganization();
    }

    /// <summary>Replaces the short name an organization's members sign in under.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="request">The new short name.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it stands, <c>404</c> when no such organization exists, <c>409</c> when another organization holds the short name, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>Every member's password works under the new short name from the moment this answers, and under the old one no longer; nothing is re-provisioned.</remarks>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> ChangeShortNameAsync(
        Guid organizationId,
        [FromBody] OrganizationShortNameRequest? request,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        if (!OrganizationShortName.TryCreate(request?.ShortName, out var shortName))
        {
            return Refused(OrganizationShortName.DescribeAcceptedForm());
        }

        var result = await organizations.ChangeShortNameAsync(organizationId, shortName, cancellationToken);

        return result.Outcome switch
        {
            OrganizationWriteOutcome.Written => TypedResults.NoContent(),
            OrganizationWriteOutcome.ShortNameTaken => ShortNameTaken(shortName),
            _ => NoSuchOrganization(),
        };
    }

    /// <summary>Removes an organization nobody belongs to.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it is gone, <c>404</c> when no such organization exists, or <c>409</c> naming how many members it still has.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> DeleteAsync(
        Guid organizationId,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        var result = await organizations.DeleteAsync(organizationId, cancellationToken);

        return result.Outcome switch
        {
            OrganizationWriteOutcome.Written => TypedResults.NoContent(),
            OrganizationWriteOutcome.StillHasMembers => TypedResults.Problem(
                $"Organization '{organizationId}' still has {result.RemainingMembers} member(s). Move each of them out "
                + "of it before removing it.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NoSuchOrganization(),
        };
    }

    /// <summary>Moves a user into an organization, or out of every organization, re-scoping their passwords with them.</summary>
    /// <param name="userId">The user being moved.</param>
    /// <param name="request">The organization to move them into, or none.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the move stands, <c>404</c> when no such user exists, <c>409</c> naming the username the target already holds, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> SetUserOrganizationAsync(
        Guid userId,
        [FromBody] UserOrganizationRequest? request,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        if (userId == Guid.Empty)
        {
            return Refused("The request named no user.");
        }

        if (request is null)
        {
            return Refused("The request named neither an organization nor that the user should belong to none.");
        }

        if (request.OrganizationId == Guid.Empty)
        {
            return Refused("An organization is named by the identifier this deployment recorded it under.");
        }

        var result = await organizations.SetUserOrganizationAsync(
            MailUserId.Create(userId),
            request.OrganizationId,
            cancellationToken);

        return result.Outcome switch
        {
            OrganizationWriteOutcome.Written => TypedResults.NoContent(),
            OrganizationWriteOutcome.UnknownUser => TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no such user.",
            }),
            OrganizationWriteOutcome.UnknownOrganization => Refused(
                $"This deployment holds no organization '{request.OrganizationId}'. List the organizations to read the "
                + "identifiers it does hold."),
            _ => TypedResults.Problem(
                result.CollidingUsername is { } username
                    ? $"The target already holds a password credential under the username '{username}', so this user "
                        + "cannot sign in there under it. Rename or remove one of the two credentials first."
                    : "The target already holds a password credential under one of this user's usernames. Rename or "
                        + "remove one of the two credentials first.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static ProblemHttpResult ShortNameTaken(OrganizationShortName shortName) => TypedResults.Problem(
        $"Another organization already signs in under '{shortName.Value}'. A short name is half of a login, so choose "
        + "another.",
        statusCode: StatusCodes.Status409Conflict);

    private static NotFound<ProblemDetails> NoSuchOrganization() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no such organization.",
    });

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
