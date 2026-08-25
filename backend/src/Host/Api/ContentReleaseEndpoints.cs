// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Release;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the release of the database copies the move left beside the objects it verified.</summary>
/// <remarks>
/// <para>
/// The move copies and never removes, so a deployment part way through one holds its mail twice: the object it now reads
/// from, and the payload the database went on holding so that a read still works while the bucket is being trusted for
/// the first time. These two routes are what an operator does about that — read how much of it there is, and end it.
/// </para>
/// <para>
/// <b>The reading is <c>mailfathom.admin.read</c> and the freeing is <c>mailfathom.admin.erase</c></b>, which is the one
/// place the content routes depart from the move's. Asking a deployment to copy its mail somewhere is work and takes the
/// operating grant; removing the last copy of it outside the bucket is disposal, and it takes the grant this deployment
/// allocates to disposing of what it holds. A credential that may start the move must not be able to end it.
/// </para>
/// <para>
/// One request is one bounded batch and the command repeats it, exactly as a folder erasure's is. Answering after each
/// batch is what makes an interrupted release resumable rather than a state nothing can finish, and it is what puts a
/// figure in front of an operator between one irreversible step and the next.
/// </para>
/// <para>
/// They are here rather than on the MCP surface for the reason every administrative route is: where a payload is stored
/// is nothing a model reasons over, and disposing of one is the last thing that should be.
/// </para>
/// </remarks>
internal static class ContentReleaseEndpoints
{
    /// <summary>The route the retained copies are read at and freed on, relative to the administrative prefix.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and performed with <c>POST</c>, which is what keeps the figure an operator confirms
    /// and the figure the deployment acts on the same figure rather than two counts that happen to agree.
    /// </remarks>
    internal const string ReleaseRoute = "/content/release";

    /// <summary>Maps the release routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapContentRelease(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ReleaseRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(ReleaseRoute, ReleaseAsync)
            .RequirePermission(MailFathomPermission.AdminErase);
    }

    /// <summary>Reports how much of this deployment's database is a copy of what the bucket already holds.</summary>
    /// <param name="release">Counts what is retained and what the move has not yet carried.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with what is retained, and with the backlog that would refuse a release.</returns>
    /// <remarks>
    /// It answers on a deployment that has moved nothing, where both figures say so. That is the point: an operator
    /// weighing whether the move is finished enough to release reads the same two numbers the release itself decides on.
    /// </remarks>
    internal static async Task<Ok<ContentReleaseResponse>> ReadAsync(
        [FromServices] RetainedContentRelease release,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        return TypedResults.Ok(ContentReleaseResponse.For(await release.ReadAsync(cancellationToken)));
    }

    /// <summary>Frees one bounded batch of the retained copies, leaving every row reading from its object alone.</summary>
    /// <param name="release">Performs the batch.</param>
    /// <param name="cancellationToken">Cancels the release between payload kinds, leaving what it has already freed.</param>
    /// <returns><c>200</c> with what was freed, or <c>409</c> when content is still waiting to be carried into the bucket.</returns>
    /// <remarks>
    /// <para>
    /// A deployment whose database still owns a payload is refused rather than partly released, because a payload the
    /// move has not carried is one no object was ever verified for. The refusal names the backlog and the move that
    /// repairs it, and it is a conflict rather than a bad request: nothing is wrong with what was asked, and asking
    /// again after the move has finished is exactly the right thing to do.
    /// </para>
    /// <para>
    /// What this removes cannot be undone from anywhere but a backup. The command asks before it sends the first batch,
    /// and this route performs what it was asked for without asking again — which is why the grant it requires is the
    /// erasing one.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ContentReleaseResponse>, ProblemHttpResult>> ReleaseAsync(
        [FromServices] RetainedContentRelease release,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        var result = await release.ReleaseAsync(cancellationToken);

        if (result.WasRefused)
        {
            return TypedResults.Problem(
                $"The database still holds {result.AwaitingMove.PayloadCount} payloads the move has not carried into the object backend, so nothing was released. Finish the move first: ask for one with a POST to the content-move route, and release the copies once it reports no backlog.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(ContentReleaseResponse.For(result));
    }
}
