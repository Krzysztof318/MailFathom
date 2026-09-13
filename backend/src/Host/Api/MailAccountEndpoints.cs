// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Administers the mail accounts this deployment holds and the users each is assigned to.</summary>
/// <remarks>
/// <para>
/// An account is named by the identifier this deployment generated, so it travels in the path: unlike a name somebody
/// typed, a UUID has no character that decides whether a route matched. A user is named in the body, because an
/// assignment is a change to the account rather than to the user.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" />, creating, saving, and assigning are
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, and erasing and ending an assignment are
/// <see cref="MailFathomPermission.AdminErase" />: ending the last assignment erases the account and every message this
/// deployment holds for it.
/// </para>
/// <para>
/// No answer carries a password, a token, or a client secret. A declaration is handed over redacted, and a save is read
/// as the difference from what the record holds.
/// </para>
/// </remarks>
internal static class MailAccountEndpoints
{
    /// <summary>The route accounts are listed at and created on, relative to the administrative prefix.</summary>
    internal const string MailAccountsRoute = "/mail-accounts";

    /// <summary>The route one account is read, saved, and erased at.</summary>
    internal const string MailAccountRoute = $"{MailAccountsRoute}/{{accountId:guid}}";

    /// <summary>The route an account nobody holds is assigned to a user at.</summary>
    internal const string AssignmentsRoute = $"{MailAccountRoute}/assignments";

    /// <summary>The route one user's assignment is ended at.</summary>
    internal const string AssignmentRemovalRoute = $"{AssignmentsRoute}/removal";

    /// <summary>Maps the account routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailAccounts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailAccountsRoute, ReadAllAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(MailAccountsRoute, CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapGet(MailAccountRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(MailAccountRoute, SaveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapDelete(MailAccountRoute, EraseAsync)
            .RequirePermission(MailFathomPermission.AdminErase);

        api.MapPost(AssignmentsRoute, AssignAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapPost(AssignmentRemovalRoute, UnassignAsync)
            .WithMetadata(new RequestSizeLimitAttribute(UserRecordEndpoints.MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminErase);
    }

    /// <summary>Lists the accounts this deployment holds.</summary>
    /// <param name="administration">The account administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the accounts, or <c>400</c> when an account's stored row is not a declaration of settings.</returns>
    internal static async Task<Results<Ok<MailAccountListResponse>, ProblemHttpResult>> ReadAllAsync(
        [FromServices] MailAccountAdministration administration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);

        MailAccountListing listing;

        try
        {
            listing = await administration.ReadAllAsync(cancellationToken);
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            // The parser's own message names the offending token, the JSON path it stopped at, and a byte position,
            // and the path is composed from the row's own key names — which for an account are its settings.
            return Refusal(UnreadableAccount);
        }

        return TypedResults.Ok(new MailAccountListResponse(
            [.. listing.Accounts.Select(MailAccountResponse.For)],
            listing.Truncated));
    }

    /// <summary>Creates an account and assigns it to one user.</summary>
    /// <param name="administration">The account administration.</param>
    /// <param name="request">The user and the declaration.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such user, or <c>400</c> when the request names nobody or carries no declaration.</returns>
    internal static async Task<Results<Ok<MailAccountWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> CreateAsync(
        [FromServices] MailAccountAdministration administration,
        [FromBody] MailAccountCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserId == Guid.Empty)
        {
            return Refusal("A mail account is created for the user named by the identifier this deployment recorded them under.");
        }

        if (request.Account is not { Length: > 0 } account)
        {
            return Refusal("A created mail account carries its declaration.");
        }

        return await administration.CreateAsync(MailUserId.Create(request.UserId), account, cancellationToken) is { } created
            ? TypedResults.Ok(MailAccountWriteResponse.For(created.Outcome, created.AccountId))
            : NotFound("This deployment holds no such user.");
    }

    /// <summary>Hands over one account, as the redacted declaration an editing session opens.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="administration">The account administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the account, <c>404</c> when this deployment holds no such account, or <c>400</c> when its stored row is not a declaration of settings.</returns>
    internal static async Task<Results<Ok<MailAccountResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReadAsync(
        Guid accountId,
        [FromServices] MailAccountAdministration administration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);

        MailAccountReading? reading;

        try
        {
            reading = await administration.ReadAsync(accountId, cancellationToken);
        }
        catch (Exception refusal) when (refusal is FormatException or JsonException)
        {
            // The parser's own message names the offending token, the JSON path it stopped at, and a byte position,
            // and the path is composed from the row's own key names — which for an account are its settings.
            return Refusal(UnreadableAccount);
        }

        return reading is null
            ? NotFound(NoSuchAccount)
            : TypedResults.Ok(MailAccountResponse.For(reading));
    }

    /// <summary>Takes back one account's declaration as an editing session saved it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="administration">The account administration.</param>
    /// <param name="request">The declaration and the version it was read at.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such account, or <c>400</c> when the request carries no declaration.</returns>
    internal static async Task<Results<Ok<MailAccountWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveAsync(
        Guid accountId,
        [FromServices] MailAccountAdministration administration,
        [FromBody] MailAccountSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version < 0)
        {
            return Refusal("A saved mail account states the version it was read at, which is never negative.");
        }

        if (request.Account is not { Length: > 0 } account)
        {
            return Refusal("A saved mail account carries its declaration. An editing session that means to change nothing sends nothing at all.");
        }

        return await administration.SaveAsync(accountId, account, request.Version, cancellationToken) is { } outcome
            ? TypedResults.Ok(MailAccountWriteResponse.For(outcome))
            : NotFound(NoSuchAccount);
    }

    /// <summary>Erases one account and everything this deployment stored for it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="administration">The account administration.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns><c>200</c> with whether an account was there to erase.</returns>
    /// <remarks>An account this deployment does not hold is reported as nothing erased rather than as a refusal, because the caller asked for a state and the deployment is in it.</remarks>
    internal static async Task<Ok<MailAccountErasureResponse>> EraseAsync(
        Guid accountId,
        [FromServices] MailAccountAdministration administration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);

        return TypedResults.Ok(new MailAccountErasureResponse(await administration.EraseAsync(accountId, cancellationToken)));
    }

    /// <summary>Assigns an account nobody holds to a user.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="administration">The account administration.</param>
    /// <param name="request">The user.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such account or user, or <c>400</c> when the request names nobody.</returns>
    internal static async Task<Results<Ok<MailAccountWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> AssignAsync(
        Guid accountId,
        [FromServices] MailAccountAdministration administration,
        [FromBody] MailAccountAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserId == Guid.Empty)
        {
            return Refusal("An assignment names the user by the identifier this deployment recorded them under.");
        }

        return await administration.AssignAsync(accountId, MailUserId.Create(request.UserId), cancellationToken) is { } outcome
            ? TypedResults.Ok(MailAccountWriteResponse.For(outcome))
            : NotFound("This deployment holds no such mail account or no such user.");
    }

    /// <summary>Ends one user's assignment to an account, erasing the account and its mail when nobody else is assigned it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="administration">The account administration.</param>
    /// <param name="request">The user.</param>
    /// <param name="cancellationToken">Cancels the write before it commits.</param>
    /// <returns><c>200</c> with what the write did, or <c>400</c> when the request names nobody.</returns>
    internal static async Task<Results<Ok<MailAccountUnassignmentResponse>, ProblemHttpResult>> UnassignAsync(
        Guid accountId,
        [FromServices] MailAccountAdministration administration,
        [FromBody] MailAccountAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administration);
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserId == Guid.Empty)
        {
            return Refusal("An ended assignment names the user by the identifier this deployment recorded them under.");
        }

        var unassignment = await administration.UnassignAsync(accountId, MailUserId.Create(request.UserId), cancellationToken);

        return TypedResults.Ok(new MailAccountUnassignmentResponse(unassignment.Unassigned, unassignment.AccountErased));
    }

    private const string NoSuchAccount = "This deployment holds no such mail account.";

    private const string UnreadableAccount =
        "A mail account this deployment holds is not a declaration of settings, so it cannot be read or edited. Correct the row where it was written.";

    private static NotFound<ProblemDetails> NotFound(string detail) => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = detail,
    });

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
