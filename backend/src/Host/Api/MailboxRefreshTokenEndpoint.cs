// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Takes the refresh token <c>mfctl mailbox authorize</c> produced and hands it to the deployment to keep.</summary>
/// <remarks>
/// <para>
/// The first administrative route that changes anything, and the request body carries a long-lived credential for a
/// named mailbox owner. That is what makes the endpoint's two startup warnings matter more here than they did for a
/// session probe: an endpoint served in clear text hands this token to anything on the path, and one with no
/// authentication method turned on accepts it from anybody who can reach the address.
/// </para>
/// <para>
/// <strong>The route is published under <c>mailfathom.admin.credentials.write</c>, and nothing else on this surface
/// is.</strong> Placing a mailbox owner's long-lived credential is nothing like reading the deployment's state or
/// retrying a job, so an operator who provisioned a credential for either of those has not thereby provisioned one that
/// can do this. A caller whose grant omits the permission is refused with it named, and a caller whose entry narrows
/// nothing holds it like every other permission the surface publishes.
/// </para>
/// <para>
/// Answered with no body at all. There is nothing to report that the caller did not send, and a response echoing any
/// part of it would be a way to read a stored credential back out of the service.
/// </para>
/// </remarks>
internal static class MailboxRefreshTokenEndpoint
{
    /// <summary>The route, relative to the administrative prefix the group is mapped beneath.</summary>
    internal const string Route = "/mailbox/refresh-token";

    /// <summary>The largest body this route reads, beyond which the request fails before anything is buffered.</summary>
    /// <remarks>
    /// The body is one account identifier and one refresh token, and the longest token any supported authorization
    /// server issues is a few kilobytes. Stated because the server's own default is measured in tens of megabytes,
    /// which for this route would let an authenticated client make the process buffer a body four orders of magnitude
    /// larger than anything it could mean.
    /// </remarks>
    internal const int MaxRequestBytes = 16 * 1024;

    /// <summary>Maps the write route into the administrative group, so it inherits that group's authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailboxRefreshToken(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // The attribute is reached for its metadata rather than as an MVC filter, and it is worth saying so because the
        // type's namespace suggests otherwise: it implements IRequestSizeLimitMetadata, which the routing pipeline
        // reads and applies to the request body feature, so the bound holds on a minimal API route with no MVC in the
        // process. A body over the limit is answered 413 before the handler is reached.
        api.MapPost(Route, StoreAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCredentialsWrite);
    }

    /// <summary>Stores one account's refresh token, or reports why it was not stored.</summary>
    /// <param name="request">The account and the token, as the client sent them.</param>
    /// <param name="recorder">The use case that checks the account and writes the token.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>204</c> once the token is stored, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c> and carries a message written here rather than the failure's own. An account this
    /// deployment does not configure is the caller's mistake in the request they wrote, not a missing resource — and
    /// answering <c>404</c> would collide with the answer a client already reads as "this port serves no administrative
    /// endpoint", turning a mistyped account into a report about the wrong thing.
    /// </remarks>
    internal static async Task<Results<NoContent, ProblemHttpResult>> StoreAsync(
        MailboxRefreshTokenRequest? request,
        MailboxRefreshTokenRecorder recorder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        if (string.IsNullOrWhiteSpace(request?.Account))
        {
            return TypedResults.Problem("The request named no mail account.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.Problem("The request carried no refresh token.", statusCode: StatusCodes.Status400BadRequest);
        }

        var accountId = MailAccountId.Create(request.Account);

        // Owned here and erased on the way out, because the request body is the one place the material arrives in a
        // form nothing can wipe; from here on it is only ever the domain value the store seals.
        using var refreshToken = MailboxRefreshToken.FromText(request.RefreshToken);

        try
        {
            await recorder.RecordAsync(accountId, refreshToken, cancellationToken);
        }
        catch (MailAccountNotAccessibleException)
        {
            return TypedResults.Problem(
                $"This deployment configures no mail account named '{accountId.Value}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.NoContent();
    }
}

/// <summary>The grant a client asks the deployment to keep for one of its accounts.</summary>
/// <param name="Account">The configured identifier of the account the grant acts for.</param>
/// <param name="RefreshToken">The refresh token the authorization server issued.</param>
/// <remarks>
/// Both fields are nullable so a body that omits one is refused with a message naming what is missing, rather than by
/// the model binder with one that names a property. <see cref="ToString" /> is redacted, so no diagnostic, log
/// template, or exception message can print the token by rendering the record it arrived in.
/// </remarks>
internal sealed record MailboxRefreshTokenRequest(string? Account, string? RefreshToken)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(MailboxRefreshTokenRequest)} {{ {this.Account} }}";
}
