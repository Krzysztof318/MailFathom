// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the move that carries content the database already holds into the object backend.</summary>
/// <remarks>
/// <para>
/// Selecting the object backend decides where the next payload is written and says nothing about the mail already
/// stored, which for a deployment that has been synchronizing a mailbox for a year is all of it. These routes are what
/// an operator does about that: they ask for the copy, stop it while the deployment is busy, set it going again, and
/// watch how far it has come.
/// </para>
/// <para>
/// Four routes rather than one taking an action in its body, because starting, pausing, and resuming are different
/// decisions and a mistyped value must not be the difference between stopping a move and starting one over. None of the
/// three carries a body at all.
/// </para>
/// <para>
/// The reading is <c>mailfathom.admin.read</c> and the three decisions are <c>mailfathom.admin.operate</c>, under
/// ADR 0012's rule: reporting where a deployment keeps its mail is reading what it holds, and asking it to rewrite where
/// it keeps it is asking it to do work.
/// </para>
/// <para>
/// They are here rather than on the MCP surface for the reason every administrative route is: where a payload is stored
/// is nothing a model reasons over, and what bounds administrative access is what should bound it.
/// </para>
/// </remarks>
internal static class ContentMoveEndpoints
{
    /// <summary>The route the move is asked for and read from, relative to the administrative prefix.</summary>
    internal const string MoveRoute = "/content/move";

    /// <summary>The route the move is stopped on.</summary>
    internal const string PauseRoute = $"{MoveRoute}/pause";

    /// <summary>The route a stopped move is set going again on.</summary>
    /// <remarks>
    /// A route of its own rather than a field on the request, because pausing and resuming are opposite decisions and a
    /// body carrying which one was meant would make a mistyped value the difference between the two.
    /// </remarks>
    internal const string ResumeRoute = $"{MoveRoute}/resume";

    /// <summary>Maps the content-move routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapContentMove(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MoveRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(MoveRoute, StartAsync)
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapPost(PauseRoute, PauseAsync)
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapPost(ResumeRoute, ResumeAsync)
            .RequirePermission(MailFathomPermission.AdminOperate);
    }

    /// <summary>Reports where the move has got to, and how much content the database still holds.</summary>
    /// <param name="control">Reports whether this deployment has an object backend at all.</param>
    /// <param name="moves">Reads the move and the backlog behind it.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the move, the backlog, and whether a move is possible here.</returns>
    /// <remarks>
    /// It answers on a deployment that stores content in the database as well, and that is the point: the backlog is what
    /// an operator weighs before selecting the other backend, so the figure has to be readable before the switch rather
    /// than only after it.
    /// </remarks>
    internal static async Task<Ok<ContentMoveStateResponse>> ReadAsync(
        [FromServices] StoredContentMoveControl control,
        [FromServices] StoredContentMoveReader moves,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(moves);

        var progress = await moves.ReadAsync(cancellationToken);

        return TypedResults.Ok(ContentMoveStateResponse.For(control.IsAvailable, progress));
    }

    /// <summary>Asks for every payload the database still holds to be carried into the bucket.</summary>
    /// <param name="control">Records the move, or reports the one already under way.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the move, or <c>400</c> when this deployment has no object backend to move into.</returns>
    /// <remarks>
    /// It records that the move is wanted and copies nothing. The passes are the deployment's own background work, so
    /// the request neither carries them nor keeps them alive — which is what stops an operator's terminal closing from
    /// stopping a move of their mailbox, and what makes this answer immediately however much mail there is.
    /// <para>
    /// A move that is already running or paused is answered with itself rather than started over, so asking twice is
    /// asking once and a paused operator's position is never discarded by somebody else's request.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ContentMoveRunResponse>, ProblemHttpResult>> StartAsync(
        [FromServices] StoredContentMoveControl control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (!control.IsAvailable)
        {
            return RefuseUnavailable();
        }

        var run = await control.StartAsync(cancellationToken);

        return TypedResults.Ok(ContentMoveRunResponse.For(run));
    }

    /// <summary>Stops the move where it is, leaving everything it has carried exactly as it is.</summary>
    /// <param name="control">Records the decision.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the move, or <c>404</c> when this deployment has never been asked for one.</returns>
    /// <remarks>
    /// Nothing is cancelled: a pass that is running reads this decision between payloads, so it finishes the one payload
    /// it holds and ends there. A move that has already finished is answered as it stands, because there is nothing to pause and saying
    /// so is the answer.
    /// </remarks>
    internal static async Task<Results<Ok<ContentMoveRunResponse>, NotFound>> PauseAsync(
        [FromServices] StoredContentMoveControl control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);

        return await control.PauseAsync(cancellationToken) is { } run
            ? TypedResults.Ok(ContentMoveRunResponse.For(run))
            : TypedResults.NotFound();
    }

    /// <summary>Sets a stopped move going again from the position it stopped at.</summary>
    /// <param name="control">Records the decision.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the move, <c>404</c> when none was ever asked for, or <c>400</c> when there is no object backend.</returns>
    internal static async Task<Results<Ok<ContentMoveRunResponse>, NotFound, ProblemHttpResult>> ResumeAsync(
        [FromServices] StoredContentMoveControl control,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (!control.IsAvailable)
        {
            return RefuseUnavailable();
        }

        return await control.ResumeAsync(cancellationToken) is { } run
            ? TypedResults.Ok(ContentMoveRunResponse.For(run))
            : TypedResults.NotFound();
    }

    /// <summary>States that this deployment has nowhere to carry its content to, and names the section that decides it.</summary>
    /// <remarks>
    /// A refusal rather than a move that starts and moves nothing, because the two look identical from a terminal for as
    /// long as an operator is willing to wait. It names the configuration rather than the endpoint, for the reason every
    /// object-storage failure does: the value is already in the file they would open.
    /// </remarks>
    private static ProblemHttpResult RefuseUnavailable() => TypedResults.Problem(
        "This deployment names no object-storage endpoint, so there is nowhere to move its stored content to. Configure ContentStorage:ObjectStorage and select that backend first.",
        statusCode: StatusCodes.Status400BadRequest);
}
