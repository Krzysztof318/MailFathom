// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Provisions, lists, rotates, disables, and removes the passwords an owner signs in with.</summary>
/// <remarks>
/// <para>
/// A username and password are the one credential this deployment holds a record of rather than reads out of an
/// operator's configuration, so they are the one credential that has to be administered over a route instead of edited
/// into a file. These are those routes, and they are on the administrative surface for the reason
/// <see cref="MailboxRefreshTokenEndpoint" />'s is: deciding who can read a person's mail is the most consequential
/// thing this endpoint does, and it is bounded by the same credential that bounds everything else administrative.
/// </para>
/// <para>
/// Reading and writing are separately granted. A listing says which credentials exist and whose they are, which is
/// <see cref="MailFathomPermission.AdminRead" />; provisioning, rotating, disabling, and deleting decide who can read
/// somebody's mail, which is <see cref="MailFathomPermission.AdminCredentialsWrite" /> — so an operator who provisioned
/// a credential to read this deployment's state has not thereby provisioned one that can mint a way into a mailbox.
/// </para>
/// <para>
/// Every act names the owner in the route as well as the credential, which is what the store's contract asks for: an
/// identifier copied out of the wrong listing answers that no such credential exists rather than rotating a password
/// out from under somebody else. The owner listing is here for the same reason — an administrator selects an owner
/// before doing any of this, and the identifier is the only handle either side has for one.
/// </para>
/// <para>
/// No answer carries a password, a hash, or anything derived from either, and no refusal quotes what was sent. A
/// password this deployment declined is described by the rule it broke, which is a sentence about the policy rather
/// than about the value — so a refusal reaching a terminal, a log, or a script's output discloses nothing that was
/// typed.
/// </para>
/// </remarks>
internal static class OwnerCredentialEndpoints
{
    /// <summary>The route the owners this deployment holds are listed at, relative to the administrative prefix.</summary>
    internal const string OwnersRoute = "/owners";

    /// <summary>The route one owner's credentials are listed and provisioned at, relative to the administrative prefix.</summary>
    internal const string OwnerCredentialsRoute = "/owners/{ownerId:guid}/credentials";

    /// <summary>The route one credential is removed at, relative to the administrative prefix.</summary>
    internal const string OwnerCredentialRoute = "/owners/{ownerId:guid}/credentials/{credentialId:guid}";

    /// <summary>The route one credential's password is replaced at, relative to the administrative prefix.</summary>
    /// <remarks>A route of its own rather than a field on the credential, because rotating a password is a different act under a different consequence from turning a credential off: one invalidates what somebody is using, the other suspends it, and a body carrying which was meant would make a mistyped value the difference between them.</remarks>
    internal const string OwnerCredentialPasswordRoute = $"{OwnerCredentialRoute}/password";

    /// <summary>The route one credential is turned on or off at, relative to the administrative prefix.</summary>
    internal const string OwnerCredentialEnablementRoute = $"{OwnerCredentialRoute}/enablement";

    /// <summary>The greatest number of owners a listing reads.</summary>
    /// <remarks>A single-owner deployment is what this system serves and the roster is bounded by the records an administrator created, so the ceiling exists to keep the query bounded rather than to be reached.</remarks>
    internal const int MaximumListedOwners = 100;

    /// <summary>The greatest request body the write routes read before refusing it.</summary>
    /// <remarks>
    /// A body here is a username and a password, both of which the policy bounds to a few hundred characters. Stated
    /// because the server's own default is measured in tens of megabytes, which for these routes would let an
    /// authenticated client make the process buffer a body five orders of magnitude larger than anything it could mean
    /// — and buffering a body that large before the password inside it is read is the one place this route could be
    /// made to spend work on a request it was always going to refuse.
    /// </remarks>
    internal const int MaxRequestBytes = 8 * 1024;

    /// <summary>Maps the credential routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOwnerCredentials(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OwnersRoute, ListOwnersAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapGet(OwnerCredentialsRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(OwnerCredentialsRoute, ProvisionAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPut(OwnerCredentialPasswordRoute, RotatePasswordAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPut(OwnerCredentialEnablementRoute, SetEnabledAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapDelete(OwnerCredentialRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);
    }

    /// <summary>Lists the owners this deployment holds records for.</summary>
    /// <param name="owners">Reads the roster.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the owner identifiers.</returns>
    /// <remarks>An administrator selects an owner before administering a credential, and this is where the identifier to select comes from; a deployment serving one person answers with one entry, which is what lets a client act without asking.</remarks>
    internal static async Task<Ok<MailOwnerListResponse>> ListOwnersAsync(
        [FromServices] IMailOwnerDirectory owners,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owners);

        var roster = await owners.ReadOwnersAsync(MaximumListedOwners, cancellationToken);

        return TypedResults.Ok(MailOwnerListResponse.For(roster));
    }

    /// <summary>Lists one owner's credentials.</summary>
    /// <param name="ownerId">The owner being asked about.</param>
    /// <param name="credentials">Reads the credentials, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the listing, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>An owner this deployment holds no record for is answered with an empty listing rather than a refusal, which is the use case's own decision and is why a caller cannot learn which owner identifiers exist by asking about them.</remarks>
    internal static async Task<Results<Ok<OwnerCredentialListResponse>, ProblemHttpResult>> ListAsync(
        Guid ownerId,
        [FromServices] OwnerPasswordCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        var held = await credentials.ReadCredentialsAsync(owner, cancellationToken);

        return TypedResults.Ok(new OwnerCredentialListResponse(
            ownerId,
            [.. held.Select(OwnerCredentialResponse.For)]));
    }

    /// <summary>Provisions a credential one owner can sign in with.</summary>
    /// <param name="ownerId">The owner the credential authenticates.</param>
    /// <param name="request">The username and password, as the client sent them.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the new credential's identifier, <c>409</c> when the username is taken, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// The password reaches this handler as a string, because that is what a JSON body deserializes to and nothing can
    /// wipe one. It is the last place in this process where that is true: from here it travels as a span into the
    /// hasher and no copy of it is made, which is why the boundary reads it once rather than storing it, echoing it, or
    /// putting it in a refusal.
    /// </remarks>
    internal static async Task<Results<Ok<OwnerCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionAsync(
        Guid ownerId,
        [FromBody] OwnerCredentialProvisioningRequest? request,
        [FromServices] OwnerPasswordCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (!OwnerCredentialUsername.TryCreate(request?.Username, out var username))
        {
            return Refused($"The request named no usable username. {OwnerCredentialUsername.DescribeAcceptedForm()}");
        }

        if (FindPasswordRefusal(request?.Password) is { } refusal)
        {
            return Refused(refusal);
        }

        var provisioning = await credentials.ProvisionAsync(
            owner,
            username,
            request!.Password.AsMemory(),
            cancellationToken);

        return provisioning.Outcome switch
        {
            OwnerCredentialWriteOutcome.Written =>
                TypedResults.Ok(new OwnerCredentialProvisionedResponse(provisioning.CredentialId)),
            OwnerCredentialWriteOutcome.UsernameTaken => TypedResults.Problem(
                $"Another credential already signs in as '{username.Value}'. A username names one credential across this "
                + "deployment, so choose another or remove the credential holding it.",
                statusCode: StatusCodes.Status409Conflict),
            _ => UnknownOwner(ownerId),
        };
    }

    /// <summary>Replaces one credential's password, which stops the previous one working at that instant.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="request">The new password, as the client sent it.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the password stands, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>Answered with no body, because there is nothing to report that the caller did not send and a response echoing any part of it would be a way to read a password back out of the service.</remarks>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RotatePasswordAsync(
        Guid ownerId,
        Guid credentialId,
        [FromBody] OwnerCredentialPasswordRequest? request,
        [FromServices] OwnerPasswordCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        if (FindPasswordRefusal(request?.Password) is { } refusal)
        {
            return Refused(refusal);
        }

        var outcome = await credentials.RotatePasswordAsync(
            owner,
            credentialId,
            request!.Password.AsMemory(),
            cancellationToken);

        return Answer(outcome, ownerId, credentialId);
    }

    /// <summary>Turns one credential on or off while it keeps its username and its password.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="request">Whether the credential should authenticate requests.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the state stands, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> SetEnabledAsync(
        Guid ownerId,
        Guid credentialId,
        [FromBody] OwnerCredentialEnablementRequest? request,
        [FromServices] OwnerPasswordCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        if (request?.Enabled is not { } enabled)
        {
            return Refused("The request said neither that the credential should authenticate requests nor that it should not.");
        }

        var outcome = await credentials.SetEnabledAsync(owner, credentialId, enabled, cancellationToken);

        return Answer(outcome, ownerId, credentialId);
    }

    /// <summary>Removes one credential and frees the username it held.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being removed.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the credential is gone, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid ownerId,
        Guid credentialId,
        [FromServices] OwnerPasswordCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        var outcome = await credentials.DeleteAsync(owner, credentialId, cancellationToken);

        return Answer(outcome, ownerId, credentialId);
    }

    /// <summary>Turns a write's outcome into the answer a client reads.</summary>
    /// <remarks>
    /// The two "unknown" outcomes are answered separately, because they are different mistakes an administrator makes
    /// and each is a correction they can act on. Neither is a <c>404</c>: the identifier was in a request the caller
    /// composed rather than a resource this surface publishes, and <c>404</c> already means "this port serves no
    /// administrative endpoint" to every client here.
    /// </remarks>
    private static Results<NoContent, ProblemHttpResult> Answer(
        OwnerCredentialWriteOutcome outcome,
        Guid ownerId,
        Guid credentialId) =>
        outcome switch
        {
            OwnerCredentialWriteOutcome.Written => TypedResults.NoContent(),
            OwnerCredentialWriteOutcome.UnknownOwner => UnknownOwner(ownerId),
            _ => Refused(
                $"Owner '{ownerId}' holds no credential '{credentialId}'. List the owner's credentials to read the "
                + "identifiers they actually hold."),
        };

    /// <summary>Reports why a password was not accepted, without repeating any part of it.</summary>
    private static string? FindPasswordRefusal(string? password) => password is null
        ? "The request carried no password."
        : OwnerPasswordPolicy.FindRefusal(password);

    private static bool TryReadOwner(Guid ownerId, out MailOwnerId owner)
    {
        if (ownerId == Guid.Empty)
        {
            owner = default;

            return false;
        }

        owner = MailOwnerId.Create(ownerId);

        return true;
    }

    private static ProblemHttpResult UnknownOwner(Guid ownerId) => Refused(
        $"This deployment holds no owner '{ownerId}'. List the owners to read the identifiers it does hold.");

    private static ProblemHttpResult EmptyOwner() => Refused("The request named no owner.");

    private static ProblemHttpResult EmptyCredential() => Refused("The request named no credential.");

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
