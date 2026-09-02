// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Provisions, lists, rotates, disables, and removes the credentials an owner's clients present.</summary>
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
/// Every act names the owner in the route as well as the credential, which is what the store's contract asks for: an
/// identifier copied out of the wrong listing answers that no such credential exists rather than rotating a secret out
/// from under somebody else. Where that identifier comes from is <see cref="OwnerRecordEndpoints" />, which holds the
/// roster because recording an owner and erasing one are acts on the same list.
/// </para>
/// <para>
/// No answer carries a password, a hash, or a key digest, and no refusal quotes what was sent. A password this
/// deployment declined is described by the rule it broke, which is a sentence about the policy rather than about the
/// value. The one secret any answer carries is a key this deployment has just minted, which exists nowhere else and is
/// reported once.
/// </para>
/// </remarks>
internal static class OwnerCredentialEndpoints
{
    /// <summary>The route one owner's credentials are listed and provisioned at, relative to the administrative prefix.</summary>
    internal const string OwnerCredentialsRoute = "/owners/{ownerId:guid}/credentials";

    /// <summary>The route one credential is removed at, relative to the administrative prefix.</summary>
    internal const string OwnerCredentialRoute = "/owners/{ownerId:guid}/credentials/{credentialId:guid}";

    /// <summary>The route what one credential is presented as is replaced at, relative to the administrative prefix.</summary>
    /// <remarks>A route of its own rather than a field on the credential, because replacing what a credential is presented as is a different act under a different consequence from turning it off: one invalidates what somebody is using, the other suspends it, and a body carrying which was meant would make a mistyped value the difference between them.</remarks>
    internal const string OwnerCredentialMaterialRoute = $"{OwnerCredentialRoute}/material";

    /// <summary>The route one credential is turned on or off at, relative to the administrative prefix.</summary>
    internal const string OwnerCredentialEnablementRoute = $"{OwnerCredentialRoute}/enablement";

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
    internal static void MapOwnerCredentials(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OwnerCredentialsRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements
        // IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body over the
        // bound is answered 413 before the handler is reached.
        api.MapPost(OwnerCredentialsRoute, ProvisionAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPut(OwnerCredentialMaterialRoute, ReplaceMaterialAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapPut(OwnerCredentialEnablementRoute, SetEnabledAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        api.MapDelete(OwnerCredentialRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);
    }

    /// <summary>Lists one owner's credentials, of every method.</summary>
    /// <param name="ownerId">The owner being asked about.</param>
    /// <param name="credentials">Reads the credentials, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the listing, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>An owner this deployment holds no record for is answered with an empty listing rather than a refusal, which is the use case's own decision and is why a caller cannot learn which owner identifiers exist by asking about them.</remarks>
    internal static async Task<Results<Ok<OwnerCredentialListResponse>, ProblemHttpResult>> ListAsync(
        Guid ownerId,
        [FromServices] OwnerCredentialAdministration credentials,
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

    /// <summary>Provisions a credential one owner's clients can present.</summary>
    /// <param name="ownerId">The owner the credential authenticates.</param>
    /// <param name="request">The method and whatever that method requires, as the client sent them.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="publicKeys">Decides whether a written public key is one this deployment accepts, so the refusal is a request the operator can correct.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the new credential, <c>409</c> when what it would be resolved by is taken or the owner already holds as many credentials as one owner may, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A password reaches this handler as a string, because that is what a JSON body deserializes to and nothing can
    /// wipe one. It is the last place in this process where that is true: from here it travels as a span into the
    /// hasher and no copy of it is made, which is why the boundary reads it once rather than storing it, echoing it, or
    /// putting it in a refusal.
    /// </remarks>
    internal static async Task<Results<Ok<OwnerCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionAsync(
        Guid ownerId,
        [FromBody] OwnerCredentialProvisioningRequest? request,
        [FromServices] OwnerCredentialAdministration credentials,
        [FromServices] IClientPublicKeyReader publicKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(publicKeys);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (!OwnerCredentialMethod.TryParse(request?.Method, out var method))
        {
            return UnknownMethod(request?.Method);
        }

        if (!TryReadGrant(request?.Permissions, out var permissions, out var grantRefusal))
        {
            return Refused(grantRefusal!);
        }

        if (method == OwnerCredentialMethod.ApiKey)
        {
            return AnswerProvisioning(
                method,
                ownerId,
                await credentials.ProvisionApiKeyAsync(owner, permissions, cancellationToken));
        }

        if (method == OwnerCredentialMethod.Password)
        {
            return await ProvisionPasswordAsync(owner, ownerId, request!, permissions, credentials, cancellationToken);
        }

        if (method == OwnerCredentialMethod.PublicKey)
        {
            if (FindPublicKeyRefusal(request!.PublicKey, publicKeys) is { } keyRefusal)
            {
                return Refused(keyRefusal);
            }

            return AnswerProvisioning(
                method,
                ownerId,
                await credentials.ProvisionPublicKeyAsync(
                    owner,
                    request.PublicKey!,
                    permissions,
                    cancellationToken));
        }

        return await ProvisionOAuthSubjectAsync(owner, ownerId, request!, permissions, credentials, cancellationToken);
    }

    /// <summary>Provisions the username and password one owner signs in with, refusing each half by the rule it broke.</summary>
    private static async Task<Results<Ok<OwnerCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionPasswordAsync(
        MailOwnerId owner,
        Guid ownerId,
        OwnerCredentialProvisioningRequest request,
        IReadOnlyList<MailFathomPermission>? permissions,
        OwnerCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        if (!OwnerCredentialUsername.TryCreate(request.Username, out var username))
        {
            return Refused($"The request named no usable username. {OwnerCredentialUsername.DescribeAcceptedForm()}");
        }

        if (FindPasswordRefusal(request.Password) is { } refusal)
        {
            return Refused(refusal);
        }

        return AnswerProvisioning(
            OwnerCredentialMethod.Password,
            ownerId,
            await credentials.ProvisionPasswordAsync(
                owner,
                username,
                request.Password.AsMemory(),
                permissions,
                cancellationToken));
    }

    /// <summary>Maps one authorization server's subject onto the owner it stands for.</summary>
    private static async Task<Results<Ok<OwnerCredentialProvisionedResponse>, ProblemHttpResult>> ProvisionOAuthSubjectAsync(
        MailOwnerId owner,
        Guid ownerId,
        OwnerCredentialProvisioningRequest request,
        IReadOnlyList<MailFathomPermission>? permissions,
        OwnerCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        if (!OwnerCredentialLookup.TryCreateForOAuthSubject(request.Issuer, request.Subject, out _))
        {
            return Refused(
                "The request accepts 'oauth-subject' and names no usable pair. Write the authorization server's issuer "
                + "exactly as it is configured, and the subject that server issues for the person.");
        }

        return AnswerProvisioning(
            OwnerCredentialMethod.OAuthSubject,
            ownerId,
            await credentials.ProvisionOAuthSubjectAsync(
                owner,
                request.Issuer,
                request.Subject,
                permissions,
                cancellationToken));
    }

    /// <summary>Replaces what one credential is presented as, which stops the previous material working at that instant.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="request">The method and its new material, as the client sent them.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="publicKeys">Decides whether a written public key is one this deployment accepts, so the refusal is a request the operator can correct.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> once the new material stands, <c>409</c> when what the credential would be resolved by is taken, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>A mapped subject is refused here rather than silently doing nothing: there is nothing about it this deployment issued, so pointing an owner at a different subject is a credential to provision rather than material to replace.</remarks>
    internal static async Task<Results<Ok<OwnerCredentialRotatedResponse>, ProblemHttpResult>> ReplaceMaterialAsync(
        Guid ownerId,
        Guid credentialId,
        [FromBody] OwnerCredentialMaterialRequest? request,
        [FromServices] OwnerCredentialAdministration credentials,
        [FromServices] IClientPublicKeyReader publicKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(publicKeys);

        if (!TryReadOwner(ownerId, out var owner))
        {
            return EmptyOwner();
        }

        if (credentialId == Guid.Empty)
        {
            return EmptyCredential();
        }

        if (!OwnerCredentialMethod.TryParse(request?.Method, out var method))
        {
            return UnknownMethod(request?.Method);
        }

        if (!method.MaterialIsReplaceable)
        {
            return Refused(
                $"A '{method.Name}' credential is presented as something this deployment did not issue, so there is "
                + "nothing here to replace. Provision the credential the owner should act under and remove this one.");
        }

        if (method == OwnerCredentialMethod.ApiKey)
        {
            return AnswerRotation(
                method,
                ownerId,
                credentialId,
                await credentials.RotateApiKeyAsync(owner, credentialId, cancellationToken));
        }

        if (method == OwnerCredentialMethod.Password)
        {
            return await RotatePasswordAsync(
                owner,
                ownerId,
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
            ownerId,
            credentialId,
            await credentials.ReplacePublicKeyAsync(
                owner,
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
    private static async Task<Results<Ok<OwnerCredentialRotatedResponse>, ProblemHttpResult>> RotatePasswordAsync(
        MailOwnerId owner,
        Guid ownerId,
        Guid credentialId,
        OwnerCredentialMaterialRequest request,
        OwnerCredentialAdministration credentials,
        CancellationToken cancellationToken)
    {
        if (!OwnerCredentialUsername.TryCreate(request.Username, out var username))
        {
            return Refused($"The request named no usable username. {OwnerCredentialUsername.DescribeAcceptedForm()}");
        }

        if (FindPasswordRefusal(request.Password) is { } refusal)
        {
            return Refused(refusal);
        }

        return AnswerRotation(
            OwnerCredentialMethod.Password,
            ownerId,
            credentialId,
            await credentials.RotatePasswordAsync(
                owner,
                credentialId,
                username,
                request.Password.AsMemory(),
                cancellationToken));
    }

    /// <summary>Turns one credential on or off while it keeps what it is presented as.</summary>
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
        [FromServices] OwnerCredentialAdministration credentials,
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

    /// <summary>Removes one credential and frees what it was resolved by.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being removed.</param>
    /// <param name="credentials">Performs the write, for a caller the use case's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the credential is gone, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid ownerId,
        Guid credentialId,
        [FromServices] OwnerCredentialAdministration credentials,
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

        refusal = OwnerCredentialAdministration.FindGrantRefusal(parsed);

        if (refusal is not null)
        {
            return false;
        }

        permissions = parsed;

        return true;
    }

    /// <summary>Turns a provisioning outcome into the answer a client reads.</summary>
    /// <remarks>The successful answer is the one place a minted key exists outside the write that drew it, which is why it carries a body where the other write routes carry none.</remarks>
    private static Results<Ok<OwnerCredentialProvisionedResponse>, ProblemHttpResult> AnswerProvisioning(
        OwnerCredentialMethod method,
        Guid ownerId,
        OwnerCredentialProvisioning provisioning) =>
        provisioning.Outcome switch
        {
            OwnerCredentialWriteOutcome.Written =>
                TypedResults.Ok(OwnerCredentialProvisionedResponse.For(method, provisioning)),
            OwnerCredentialWriteOutcome.LookupTaken => LookupTaken(method),
            OwnerCredentialWriteOutcome.OwnerAtCredentialCeiling => TypedResults.Problem(
                $"Owner '{ownerId}' already holds the {OwnerCredential.MaximumListedPerOwner} credentials one owner "
                + "may hold. Remove one that is no longer used before provisioning another.",
                statusCode: StatusCodes.Status409Conflict),
            OwnerCredentialWriteOutcome.UnknownOwner => UnknownOwner(ownerId),
            _ => Refused(
                $"Provisioning a credential for owner '{ownerId}' was refused for a reason this deployment cannot "
                + "describe. List the owner's credentials to read what it holds."),
        };

    /// <summary>Turns a rotation outcome into the answer a client reads.</summary>
    private static Results<Ok<OwnerCredentialRotatedResponse>, ProblemHttpResult> AnswerRotation(
        OwnerCredentialMethod method,
        Guid ownerId,
        Guid credentialId,
        OwnerCredentialRotation rotation) =>
        rotation.Outcome switch
        {
            OwnerCredentialWriteOutcome.Written =>
                TypedResults.Ok(OwnerCredentialRotatedResponse.For(method, rotation)),
            OwnerCredentialWriteOutcome.LookupTaken => LookupTaken(method),
            OwnerCredentialWriteOutcome.UnknownOwner => UnknownOwner(ownerId),
            _ => Refused(
                $"Owner '{ownerId}' holds no '{method.Name}' credential '{credentialId}'. List the owner's credentials "
                + "to read the identifiers and methods they actually hold."),
        };

    /// <summary>Reports why a password was not accepted, without repeating any part of it.</summary>
    private static string? FindPasswordRefusal(string? password) => password is null
        ? "The request carried no password."
        : OwnerPasswordPolicy.FindRefusal(password);

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

    private static ProblemHttpResult UnknownMethod(string? written) => Refused(
        $"{Describe(written)} names no credential method this deployment publishes; write one of "
        + $"{string.Join(", ", OwnerCredentialMethod.All.Select(method => $"'{method.Name}'"))}.");

    /// <summary>Reports that something else already resolves to the value this credential would have been found by.</summary>
    /// <remarks>What the collision is called depends on the method and is worth naming, because each of the four is a different thing an administrator has to go and look at.</remarks>
    private static ProblemHttpResult LookupTaken(OwnerCredentialMethod method) => TypedResults.Problem(
        $"Another credential is already resolved by what this '{method.Name}' credential would be resolved by. "
        + "One value resolves one credential across this deployment, so choose another or remove the credential "
        + "holding it.",
        statusCode: StatusCodes.Status409Conflict);

    private static string Describe(string? written) =>
        string.IsNullOrWhiteSpace(written) ? "A request naming no method" : $"'{written}'";

    private static ProblemHttpResult EmptyOwner() => Refused("The request named no owner.");

    private static ProblemHttpResult EmptyCredential() => Refused("The request named no credential.");

    private static ProblemHttpResult Refused(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
