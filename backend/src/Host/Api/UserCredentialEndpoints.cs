// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Provisions, lists, rotates, disables, and removes the credentials a user's clients present.</summary>
/// <remarks>
/// <para>
/// Every credential that reaches somebody's mail is a record this deployment holds rather than a value in an operator's
/// configuration, so every one of them is administered over a route instead of edited into a file. These are those
/// routes, and they are on the administrative surface for the reason
/// <see cref="MailboxRefreshTokenEndpoint" />'s is: deciding who can read a person's mail is the most consequential
/// thing this endpoint does, and it is bounded by the same credential that bounds everything else administrative.
/// </para>
/// <para>
/// One group of routes for four methods, rather than a group per method. What differs between a password, a key, a
/// registered public key, and a mapped subject is what is presented and what this deployment keeps of it; what an
/// administrator does with them is identical — provision, list, rotate, disable, delete — so the method is a field on
/// the request rather than a fifth path segment somebody has to learn.
/// </para>
/// <para>
/// Reading and writing are separately granted. A listing says which credentials exist and whose they are, which is
/// <see cref="MailFathomPermission.AdminRead" />; provisioning, rotating, disabling, and deleting decide who can read
/// somebody's mail, which is <see cref="MailFathomPermission.AdminCredentialsWrite" /> — so an operator who provisioned
/// a credential to read this deployment's state has not thereby provisioned one that can mint a way into a mailbox.
/// </para>
/// <para>
/// Every act names the user in the route as well as the credential, which is what the store's contract asks for: an
/// identifier copied out of the wrong listing answers that no such credential exists rather than rotating a secret out
/// from under somebody else. Where that identifier comes from is <see cref="UserRecordEndpoints" />, which holds the
/// roster because recording a user and erasing one are acts on the same list.
/// </para>
/// <para>
/// No answer carries a password, a hash, or a key digest, and no refusal quotes what was sent. A password this
/// deployment declined is described by the rule it broke, which is a sentence about the policy rather than about the
/// value. The one secret any answer carries is a key this deployment has just minted, which exists nowhere else and is
/// reported once.
/// </para>
/// </remarks>
internal static class UserCredentialEndpoints
{
    /// <summary>The route one user's credentials are listed and provisioned at, relative to the administrative prefix.</summary>
    internal const string UserCredentialsRoute = "/users/{userId:guid}/credentials";

    /// <summary>The route one credential is removed at, relative to the administrative prefix.</summary>
    internal const string UserCredentialRoute = "/users/{userId:guid}/credentials/{credentialId:guid}";

    /// <summary>The route what one credential is presented as is replaced at, relative to the administrative prefix.</summary>
    /// <remarks>A route of its own rather than a field on the credential, because replacing what a credential is presented as is a different act under a different consequence from turning it off: one invalidates what somebody is using, the other suspends it, and a body carrying which was meant would make a mistyped value the difference between them.</remarks>
    internal const string UserCredentialMaterialRoute = $"{UserCredentialRoute}/material";

    /// <summary>The route one credential is turned on or off at, relative to the administrative prefix.</summary>
    internal const string UserCredentialEnablementRoute = $"{UserCredentialRoute}/enablement";

    /// <summary>The greatest request body the write routes read before refusing it.</summary>
    /// <remarks>
    /// A body here is a username, a password, a public key, or an issuer and a subject, every one of which the policy
    /// or the reader bounds. Stated because the server's own default is measured in tens of megabytes, which for these
    /// routes would let an authenticated client make the process buffer a body five orders of magnitude larger than
    /// anything it could mean — and buffering a body that large before the secret inside it is read is the one place
    /// this route could be made to spend work on a request it was always going to refuse.
    /// </remarks>
    internal const int MaxRequestBytes = 8 * 1024;

    /// <summary>Maps the credential routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapUserCredentials(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(UserCredentialsRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(UserCredentialsRoute, ProvisionAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPut(UserCredentialMaterialRoute, ReplaceMaterialAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPut(UserCredentialEnablementRoute, SetEnabledAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapDelete(UserCredentialRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);
    }

    /// <summary>Lists one user's credentials, of every method.</summary>
    /// <param name="userId">The user being asked about.</param>
    /// <param name="credentials">Reads the credentials, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the listing, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>A user this deployment holds no record for is answered with an empty listing rather than a refusal, which is the use case's own decision and is why a caller cannot learn which user identifiers exist by asking about them.</remarks>
    internal static async Task<Results<Ok<UserCredentialListResponse>, ProblemHttpResult>> ListAsync(
        Guid userId,
        [FromServices] UserCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadUser(userId, out var user))
        {
            return EmptyUser();
        }

        var held = await credentials.ReadCredentialsAsync(user, cancellationToken);

        return TypedResults.Ok(new UserCredentialListResponse(
            userId,
            [.. held.Select(UserCredentialResponse.For)]));
    }

    /// <summary>Provisions a credential one user's clients can present.</summary>
    /// <param name="userId">The user the credential authenticates.</param>
    /// <param name="request">The method and whatever that method requires, as the client sent them.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="publicKeys">Decides whether a written public key is one this deployment accepts, so the refusal is a request the operator can correct.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the new credential, <c>409</c> when what it would be resolved by is taken or the user already holds as many credentials as one user may, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A password reaches this handler as a string, because that is what a JSON body deserializes to and nothing can
    /// wipe one. It is the last place in this process where that is true: from here it travels as a span into the
    /// hasher and no copy of it is made, which is why the boundary reads it once rather than storing it, echoing it, or
    /// putting it in a refusal.
    /// </remarks>
    internal static async Task<Results<Ok<UserCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionAsync(
        Guid userId,
        [FromBody] UserCredentialProvisioningRequest? request,
        [FromServices] UserCredentialAdministration credentials,
        [FromServices] IClientPublicKeyReader publicKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(publicKeys);

        if (!TryReadUser(userId, out var user))
        {
            return EmptyUser();
        }

        if (!UserCredentialMethod.TryParse(request?.Method, out var method))
        {
            return UnknownMethod(request?.Method);
        }

        if (!TryReadGrant(request?.Permissions, out var permissions, out var grantRefusal))
        {
            return Refused(grantRefusal!);
        }

        if (method == UserCredentialMethod.ApiKey)
        {
            return AnswerProvisioning(
                method,
                userId,
                await credentials.ProvisionApiKeyAsync(user, permissions, cancellationToken));
        }

        if (method == UserCredentialMethod.Password)
        {
            return await ProvisionPasswordAsync(user, userId, request!, permissions, credentials, cancellationToken);
        }

        if (method == UserCredentialMethod.PublicKey)
        {
            if (FindPublicKeyRefusal(request!.PublicKey, publicKeys) is { } keyRefusal)
            {
                return Refused(keyRefusal);
            }

            return AnswerProvisioning(
                method,
                userId,
                await credentials.ProvisionPublicKeyAsync(
                    user,
                    request.PublicKey!,
                    permissions,
                    cancellationToken));
        }

        return await ProvisionOAuthSubjectAsync(user, userId, request!, permissions, credentials, cancellationToken);
    }

    /// <summary>Provisions the username and password one user signs in with, refusing each half by the rule it broke.</summary>
    private static async Task<Results<Ok<UserCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionPasswordAsync(
        MailUserId user,
        Guid userId,
        UserCredentialProvisioningRequest request,
        IReadOnlyList<MailFathomPermission>? permissions,
        UserCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        if (!UserCredentialUsername.TryCreate(request.Username, out var username))
        {
            return Refused($"The request named no usable username. {UserCredentialUsername.DescribeAcceptedForm()}");
        }

        if (FindPasswordRefusal(request.Password) is { } refusal)
        {
            return Refused(refusal);
        }

        return AnswerProvisioning(
            UserCredentialMethod.Password,
            userId,
            await credentials.ProvisionPasswordAsync(
                user,
                username,
                request.Password.AsMemory(),
                permissions,
                cancellationToken));
    }

    /// <summary>Maps one authorization server's subject onto the user it stands for.</summary>
    private static async Task<Results<Ok<UserCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionOAuthSubjectAsync(
        MailUserId user,
        Guid userId,
        UserCredentialProvisioningRequest request,
        IReadOnlyList<MailFathomPermission>? permissions,
        UserCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        if (!UserCredentialLookup.TryCreateForOAuthSubject(request.Issuer, request.Subject, out _))
        {
            return Refused(
                "The request accepts 'oauth-subject' and names no usable pair. Write the authorization server's issuer "
                + "exactly as it is configured, and the subject that server issues for the person.");
        }

        return AnswerProvisioning(
            UserCredentialMethod.OAuthSubject,
            userId,
            await credentials.ProvisionOAuthSubjectAsync(
                user,
                request.Issuer,
                request.Subject,
                permissions,
                cancellationToken));
    }

    /// <summary>Replaces what one credential is presented as, which stops the previous material working at that instant.</summary>
    /// <param name="userId">The user the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="request">The method and its new material, as the client sent them.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="publicKeys">Decides whether a written public key is one this deployment accepts, so the refusal is a request the operator can correct.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> once the new material stands, <c>409</c> when what the credential would be resolved by is taken, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>A mapped subject is refused here rather than silently doing nothing: there is nothing about it this deployment issued, so pointing a user at a different subject is a credential to provision rather than material to replace.</remarks>
    internal static async Task<Results<Ok<UserCredentialRotatedResponse>, ProblemHttpResult>> ReplaceMaterialAsync(
        Guid userId,
        Guid credentialId,
        [FromBody] UserCredentialMaterialRequest? request,
        [FromServices] UserCredentialAdministration credentials,
        [FromServices] IClientPublicKeyReader publicKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(publicKeys);

        if (!TryReadUser(userId, out var user))
        {
            return EmptyUser();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        if (!UserCredentialMethod.TryParse(request?.Method, out var method))
        {
            return UnknownMethod(request?.Method);
        }

        if (!method.MaterialIsReplaceable)
        {
            return Refused(
                $"A '{method.Name}' credential is presented as something this deployment did not issue, so there is "
                + "nothing here to replace. Provision the credential the user should act under and remove this one.");
        }

        if (method == UserCredentialMethod.ApiKey)
        {
            return AnswerRotation(
                method,
                userId,
                credentialId,
                await credentials.RotateApiKeyAsync(user, credentialId, cancellationToken));
        }

        if (method == UserCredentialMethod.Password)
        {
            return await RotatePasswordAsync(
                user,
                userId,
                credentialId,
                request!,
                credentials,
                cancellationToken);
        }

        if (FindPublicKeyRefusal(request!.PublicKey, publicKeys, replacing: true) is { } keyRefusal)
        {
            return Refused(keyRefusal);
        }

        return AnswerRotation(
            method,
            userId,
            credentialId,
            await credentials.ReplacePublicKeyAsync(
                user,
                credentialId,
                request.PublicKey!,
                cancellationToken));
    }

    /// <summary>Reports why a written public key cannot be read, or <see langword="null" /> when it can.</summary>
    /// <param name="written">The key as the request carried it.</param>
    /// <param name="publicKeys">The reader that decides what a written key may be.</param>
    /// <param name="replacing">Whether the key is replacing one, which is the only difference between the two sentences.</param>
    /// <returns>The refusal, or <see langword="null" /> when the key is one this deployment accepts.</returns>
    /// <remarks>
    /// The boundary reads the key rather than letting the use case raise on it. Nothing in this process maps that
    /// exception to a response, so an operator pasting a private key or a truncated PEM would be answered with a
    /// <c>500</c> instead of the sentence the reader publishes for exactly that mistake.
    /// </remarks>
    private static string? FindPublicKeyRefusal(
        string? written,
        IClientPublicKeyReader publicKeys,
        bool replacing = false)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return replacing
                ? "The request names 'public-key' and carried none. Write the client's new public key."
                : "The request accepts 'public-key' and carried none. Write the client's public key.";
        }

        return publicKeys.TryRead(written, out _)
            ? null
            : publicKeys.DescribeAcceptedForm();
    }

    /// <summary>Replaces one credential's password, refusing each half by the rule it broke.</summary>
    private static async Task<Results<Ok<UserCredentialRotatedResponse>, ProblemHttpResult>> RotatePasswordAsync(
        MailUserId user,
        Guid userId,
        Guid credentialId,
        UserCredentialMaterialRequest request,
        UserCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        if (!UserCredentialUsername.TryCreate(request.Username, out var username))
        {
            return Refused($"The request named no usable username. {UserCredentialUsername.DescribeAcceptedForm()}");
        }

        if (FindPasswordRefusal(request.Password) is { } refusal)
        {
            return Refused(refusal);
        }

        return AnswerRotation(
            UserCredentialMethod.Password,
            userId,
            credentialId,
            await credentials.RotatePasswordAsync(
                user,
                credentialId,
                username,
                request.Password.AsMemory(),
                cancellationToken));
    }

    /// <summary>Turns one credential on or off while it keeps what it is presented as.</summary>
    /// <param name="userId">The user the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="request">Whether the credential should authenticate requests.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the state stands, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> SetEnabledAsync(
        Guid userId,
        Guid credentialId,
        [FromBody] UserCredentialEnablementRequest? request,
        [FromServices] UserCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadUser(userId, out var user))
        {
            return EmptyUser();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        if (request?.Enabled is not { } enabled)
        {
            return Refused("The request said neither that the credential should authenticate requests nor that it should not.");
        }

        var outcome = await credentials.SetEnabledAsync(user, credentialId, enabled, cancellationToken);

        return Answer(outcome, userId, credentialId);
    }

    /// <summary>Removes one credential and frees what it was resolved by.</summary>
    /// <param name="userId">The user the credential belongs to.</param>
    /// <param name="credentialId">The credential being removed.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the credential is gone, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid userId,
        Guid credentialId,
        [FromServices] UserCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!TryReadUser(userId, out var user))
        {
            return EmptyUser();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        var outcome = await credentials.DeleteAsync(user, credentialId, cancellationToken);

        return Answer(outcome, userId, credentialId);
    }

    /// <summary>Reads the grant a request named, or reports the sentence naming what to write instead.</summary>
    /// <remarks>An unwritten grant is not a refusal and is not an empty one: it is the whole mail surface, which is the reading a configuration entry writing no grant already had. An empty list is the opposite statement, and is passed through as one.</remarks>
    private static bool TryReadGrant(
        IReadOnlyList<string>? written,
        out IReadOnlyList<MailFathomPermission>? permissions,
        out string? refusal)
    {
        permissions = null;
        refusal = null;

        if (written is null)
        {
            return true;
        }

        var parsed = new List<MailFathomPermission>(written.Count);

        foreach (var name in written)
        {
            if (!MailFathomPermission.TryParse(name, out var permission))
            {
                refusal = $"'{name}' is not a permission MailFathom publishes.";

                return false;
            }

            parsed.Add(permission);
        }

        refusal = UserCredentialAdministration.FindGrantRefusal(parsed);

        if (refusal is not null)
        {
            return false;
        }

        permissions = parsed;

        return true;
    }

    /// <summary>Turns a provisioning outcome into the answer a client reads.</summary>
    /// <remarks>The successful answer is the one place a minted key exists outside the write that drew it, which is why it carries a body where the other write routes carry none.</remarks>
    private static Results<Ok<UserCredentialProvisionedResponse>, ProblemHttpResult> AnswerProvisioning(
        UserCredentialMethod method,
        Guid userId,
        UserCredentialProvisioning provisioning) =>
        provisioning.Outcome switch
        {
            UserCredentialWriteOutcome.Written =>
                TypedResults.Ok(UserCredentialProvisionedResponse.For(method, provisioning)),
            UserCredentialWriteOutcome.LookupTaken => LookupTaken(method),
            UserCredentialWriteOutcome.UserAtCredentialCeiling => TypedResults.Problem(
                $"User '{userId}' already holds the {UserCredential.MaximumListedPerUser} credentials one user "
                + "may hold. Remove one that is no longer used before provisioning another.",
                statusCode: StatusCodes.Status409Conflict),
            UserCredentialWriteOutcome.UnknownUser => UnknownUser(userId),
            _ => Refused(
                $"Provisioning a credential for user '{userId}' was refused for a reason this deployment cannot "
                + "describe. List the user's credentials to read what it holds."),
        };

    /// <summary>Turns a rotation outcome into the answer a client reads.</summary>
    private static Results<Ok<UserCredentialRotatedResponse>, ProblemHttpResult> AnswerRotation(
        UserCredentialMethod method,
        Guid userId,
        Guid credentialId,
        UserCredentialRotation rotation) =>
        rotation.Outcome switch
        {
            UserCredentialWriteOutcome.Written =>
                TypedResults.Ok(UserCredentialRotatedResponse.For(method, rotation)),
            UserCredentialWriteOutcome.LookupTaken => LookupTaken(method),
            UserCredentialWriteOutcome.UnknownUser => UnknownUser(userId),
            _ => Refused(
                $"User '{userId}' holds no '{method.Name}' credential '{credentialId}'. List the user's credentials "
                + "to read the identifiers and methods they actually hold."),
        };

    /// <summary>Reports why a password was not accepted, without repeating any part of it.</summary>
    private static string? FindPasswordRefusal(string? password) => password is null
        ? "The request carried no password."
        : UserPasswordPolicy.FindRefusal(password);

    /// <summary>Turns a write's outcome into the answer a client reads.</summary>
    /// <remarks>
    /// The two "unknown" outcomes are answered separately, because they are different mistakes an administrator makes
    /// and each is a correction they can act on. Neither is a <c>404</c>: the identifier was in a request the caller
    /// composed rather than a resource this surface publishes, and <c>404</c> already means "this port serves no
    /// administrative endpoint" to every client here.
    /// </remarks>
    private static Results<NoContent, ProblemHttpResult> Answer(
        UserCredentialWriteOutcome outcome,
        Guid userId,
        Guid credentialId) =>
        outcome switch
        {
            UserCredentialWriteOutcome.Written => TypedResults.NoContent(),
            UserCredentialWriteOutcome.UnknownUser => UnknownUser(userId),
            _ => Refused(
                $"User '{userId}' holds no credential '{credentialId}'. List the user's credentials to read the "
                + "identifiers they actually hold."),
        };

    private static bool TryReadUser(Guid userId, out MailUserId user)
    {
        if (userId == Guid.Empty)
        {
            user = default;

            return false;
        }

        user = MailUserId.Create(userId);

        return true;
    }

    private static ProblemHttpResult UnknownUser(Guid userId) => Refused(
        $"This deployment holds no user '{userId}'. List the users to read the identifiers it does hold.");

    private static ProblemHttpResult UnknownMethod(string? written) => Refused(
        $"{Describe(written)} names no credential method this deployment publishes; write one of "
        + $"{string.Join(", ", UserCredentialMethod.All.Select(method => $"'{method.Name}'"))}.");

    /// <summary>Reports that something else already resolves to the value this credential would have been found by.</summary>
    /// <remarks>What the collision is called depends on the method and is worth naming, because each of the four is a different thing an administrator has to go and look at.</remarks>
    private static ProblemHttpResult LookupTaken(UserCredentialMethod method) => TypedResults.Problem(
        $"Another credential is already resolved by what this '{method.Name}' credential would be resolved by. "
        + "One value resolves one credential across this deployment, so choose another or remove the credential "
        + "holding it.",
        statusCode: StatusCodes.Status409Conflict);

    private static string Describe(string? written) =>
        string.IsNullOrWhiteSpace(written) ? "A request naming no method" : $"'{written}'";

    private static ProblemHttpResult EmptyUser() => Refused("The request named no user.");

    private static ProblemHttpResult EmptyCredential() => Refused("The request named no credential.");

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
