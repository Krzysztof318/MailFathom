// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Records, lists, renames, and removes organizations, and moves a user or a mail account into or out of one.</summary>
/// <remarks>
/// <para>
/// An organization groups users and mail accounts and scopes a Basic username: a member signs in as
/// <c>SHORTNAME/username</c>. So these routes decide how the deployment's people are named when they sign in and who a
/// mailbox may be assigned to, and nothing about what an assigned user reads.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" />, recording, renaming, and removing an organization and moving
/// a mail account are <see cref="MailFathomPermission.AdminConfigurationWrite" />, and changing a short name and moving a
/// user are <see cref="MailFathomPermission.AdminCredentialsWrite" />, for the reasons
/// <see cref="OrganizationAdministration" /> gives. The listing, the display name, and the user's move name a target
/// and are admitted at a scope covering it; recording, removing, and changing the short name of an organization are the
/// deployment's alone.
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

    /// <summary>The route the organization one mail account belongs to is set or cleared at.</summary>
    internal const string MailAccountOrganizationRoute = $"{MailAccountEndpoints.MailAccountRoute}/organization";

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
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPost(OrganizationsRoute, CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPut(OrganizationDisplayNameRoute, RenameAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminConfigurationWrite);

        api.MapPut(OrganizationShortNameRoute, ChangeShortNameAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapDelete(OrganizationRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPut(UserOrganizationRoute, SetUserOrganizationAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminCredentialsWrite);

        api.MapPost(MailAccountOrganizationRoute, SetMailAccountOrganizationAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);
    }

    /// <summary>Lists one page of the organizations the caller's scopes cover.</summary>
    /// <param name="pageSize">How many organizations the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page's organizations in identifier order, the rows on it this build will not read as one, and the cursor the following page is asked with; or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<OrganizationListResponse>, ProblemHttpResult>> ListAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        if (!AdminListingRequest.TryResolve(
                AdministrativeListing.Organizations,
                pageSize,
                cursor,
                out var query,
                out var refusal))
        {
            return refusal;
        }

        var held = await organizations.ReadAsync(query, cancellationToken);

        return TypedResults.Ok(new OrganizationListResponse(
            [.. held.Organizations.Select(OrganizationResponse.For)],
            [.. held.Unreadable.Select(UnreadableOrganizationResponse.For)],
            AdminListingRequest.NextCursor(AdministrativeListing.Organizations, held.ContinuesAfter)));
    }

    /// <summary>Records an organization.</summary>
    /// <param name="request">The display name and the short name.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the minted identifier, <c>409</c> when the short name is taken, or <c>400</c> naming what was wrong with the request.</returns>
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

    /// <summary>Removes an organization that has neither members nor mail accounts.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once it is gone, <c>404</c> when no such organization exists, or <c>409</c> naming how many members and mail accounts it still has.</returns>
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
            OrganizationWriteOutcome.StillHasMembers or OrganizationWriteOutcome.StillHoldsMailAccounts => TypedResults.Problem(
                $"Organization '{organizationId}' still has {result.RemainingMembers} member(s) and "
                + $"{result.RemainingMailAccounts} mail account(s). Move each of them out of it before removing it.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NoSuchOrganization(),
        };
    }

    /// <summary>Moves a user into an organization, or out of every organization, re-scoping their passwords with them.</summary>
    /// <param name="userId">The user being moved.</param>
    /// <param name="request">The organization to move them into, or none.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the move stands, <c>404</c> when no such user exists, <c>409</c> naming the username the target already holds or how many of the user's mail accounts it would leave outside, or <c>400</c> naming what was wrong with the request.</returns>
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

        if (FindTargetRefusal(request?.OrganizationId, request?.None, "the user") is { } refusal)
        {
            return Refused(refusal);
        }

        var target = request!.None == true ? null : request.OrganizationId;

        var result = await organizations.SetUserOrganizationAsync(
            UserId.Create(userId),
            target,
            cancellationToken);

        return result.Outcome switch
        {
            OrganizationWriteOutcome.Written => TypedResults.NoContent(),
            OrganizationWriteOutcome.UnknownUser => TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no such user.",
            }),
            OrganizationWriteOutcome.UnknownOrganization => UnknownTarget(target),
            OrganizationWriteOutcome.AssignmentsOutsideOrganization => TypedResults.Problem(
                $"The user is assigned {result.StandingAssignments} mail account(s) outside the organization they would "
                + "move into, and an account is assigned only to a user of its own organization. Unassign them, or move "
                + "each account to where the user is going first.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(
                result.CollidingUsername is { } username
                    ? $"The target already holds a password credential under the username '{username}', so this user "
                        + "cannot sign in there under it. Rename or remove one of the two credentials first."
                    : "The target already holds a password credential under one of this user's usernames. Rename or "
                        + "remove one of the two credentials first.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    /// <summary>Moves a mail account into an organization, or out of every organization.</summary>
    /// <param name="accountId">The mail account being moved.</param>
    /// <param name="request">The organization to move it into, or none.</param>
    /// <param name="organizations">The organization administration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the move stands, <c>404</c> when no such mail account exists, <c>409</c> naming how many of its users it would leave outside, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>A move of an account decides who it may be assigned to and nothing about how anybody signs in, so it takes the grant an assignment takes rather than the one a move of a user takes.</remarks>
    internal static async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemHttpResult>> SetMailAccountOrganizationAsync(
        Guid accountId,
        [FromBody] MailAccountOrganizationRequest? request,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        if (accountId == Guid.Empty)
        {
            return Refused("The request named no mail account.");
        }

        if (FindTargetRefusal(request?.OrganizationId, request?.None, "the mail account") is { } refusal)
        {
            return Refused(refusal);
        }

        var target = request!.None == true ? null : request.OrganizationId;

        var result = await organizations.SetMailAccountOrganizationAsync(accountId, target, cancellationToken);

        return result.Outcome switch
        {
            OrganizationWriteOutcome.Written => TypedResults.NoContent(),
            OrganizationWriteOutcome.UnknownMailAccount => TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = "This deployment holds no such mail account.",
            }),
            OrganizationWriteOutcome.AssignmentsOutsideOrganization => TypedResults.Problem(
                $"The mail account is assigned to {result.StandingAssignments} user(s) outside the organization it would "
                + "move into, and an account is assigned only to users of its own organization. Unassign them, or move "
                + "each user to where the account is going first.",
                statusCode: StatusCodes.Status409Conflict),
            _ => UnknownTarget(target),
        };
    }

    /// <summary>Reports why a move's body names no single target, or that it names one.</summary>
    /// <param name="organizationId">The organization the body named.</param>
    /// <param name="none">Whether the body said the moved record belongs to none.</param>
    /// <param name="moved">What is being moved, as a refusal names it.</param>
    /// <returns>The sentence the request is refused with, or <see langword="null" /> when it names exactly one target.</returns>
    private static string? FindTargetRefusal(Guid? organizationId, bool? none, string moved) =>
        (organizationId, none == true) switch
        {
            (null, false) => $"The request named neither an organization nor that {moved} should belong to none.",
            ({ } named, true) => $"The request both named organization '{named}' and said {moved} should belong to none. "
                + "Send one of the two.",
            ({ } named, false) when named == Guid.Empty =>
                "An organization is named by the identifier this deployment recorded it under.",
            _ => null,
        };

    private static ProblemHttpResult UnknownTarget(Guid? target) => Refused(
        $"This deployment holds no organization '{target}'. List the organizations to read the identifiers it does hold.");

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
