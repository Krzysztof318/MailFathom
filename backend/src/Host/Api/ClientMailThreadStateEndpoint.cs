// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves where one conversation stands, as the block a client draws beside it.</summary>
/// <remarks>
/// <para>
/// A route of its own rather than a member of the conversation document, because the two are produced differently and
/// change at different times: the conversation is assembled from stored mail on every read, and the state is a record
/// written by a pass behind the account run. Folding it into the thread route would make every reader of a
/// conversation wait on a table the derivation may not have reached yet, and would re-send the whole block on every
/// page of a long exchange.
/// </para>
/// <para>
/// <b>Absence is a state rather than a failure.</b> A <c>404</c> here means this deployment has no state for that
/// conversation, and it means the same whether the derivation is turned off, has not reached the conversation yet, or
/// the conversation is one this user does not hold. A client draws that as a conversation nothing has been derived
/// about — never as an error — which is also what keeps the route from telling a caller that somebody else's exchange
/// exists.
/// </para>
/// <para>
/// It publishes no participants. A conversation's authors are in the mail itself and the conversation route already
/// carries them, counted and ordered, so asking a model for a second list would be paying a provider to restate what
/// the mailbox states exactly. A client draws its participants card from the conversation document beside this one.
/// </para>
/// <para>
/// Every statement carries its sources as the presentation plan's own citation targets, so a reader follows a source
/// here through the same <c>POST /citations/resolution</c> a Discover answer's sources are followed through, and a
/// client has one implementation of that affordance rather than two.
/// </para>
/// <para>
/// It speaks to no mail server and to no provider: a request reads a stored record, and nothing on this path can start
/// a derivation.
/// </para>
/// </remarks>
internal static class ClientMailThreadStateEndpoint
{
    /// <summary>The route reporting where one conversation stands, relative to the client prefix.</summary>
    internal const string MailThreadStateRoute = "/threads/{threadId:guid}/state";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailThreadState(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailThreadStateRoute, ReadStateAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves the derived state of one of the acting user's conversations, or reports that there is none.</summary>
    /// <param name="threadId">The conversation to read, as a message row published it.</param>
    /// <param name="state">Reads the stored state, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the state, <c>404</c> where this deployment has none for that conversation, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    internal static async Task<Results<Ok<ClientMailThreadStateResponse>, NotFound>> ReadStateAsync(
        [FromRoute] Guid threadId,
        [FromServices] MailThreadStateBrowser state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (threadId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var derived = await state.ReadStateAsync(EmailThreadId.Create(threadId), cancellationToken);

        return derived is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ClientMailThreadStateResponse.For(derived));
    }
}

/// <summary>Where one conversation stands, as the client endpoint serves it.</summary>
/// <param name="ThreadId">The conversation, as the request named it.</param>
/// <param name="Coverage">Whether the whole conversation was read, or it runs past what one derivation may read at all.</param>
/// <param name="DerivedAt">When the state was derived, which is what a client says the block is as of.</param>
/// <param name="Entries">The statements, in aspect order and within an aspect in the order the derivation put them.</param>
/// <remarks>
/// <c>coverage</c> is what a conversation too large to read in one bound is published as, and it arrives with no
/// entries: a partial reading of a long exchange presented as its state would be worse than saying nothing, so the
/// record says the conversation is beyond the bound and a client says so where the cards would be.
/// </remarks>
internal sealed record ClientMailThreadStateResponse(
    Guid ThreadId,
    string Coverage,
    DateTimeOffset DerivedAt,
    IReadOnlyList<ClientMailThreadStateEntryResponse> Entries)
{
    /// <summary>Describes one conversation's state for the wire.</summary>
    /// <param name="state">The state the reader returned.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state" /> is <see langword="null" />.</exception>
    internal static ClientMailThreadStateResponse For(EmailThreadState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new ClientMailThreadStateResponse(
            state.ThreadId.Value,
            state.Coverage.ToString(),
            state.DerivedAt,
            [.. state.Entries.Select(ClientMailThreadStateEntryResponse.For)]);
    }
}

/// <summary>One statement about where a conversation stands.</summary>
/// <param name="Aspect">Which of the four the statement is: <c>Agreement</c>, <c>OpenQuestion</c>, <c>Commitment</c>, or <c>VersionDifference</c>.</param>
/// <param name="Text">The statement itself, in the language the conversation is written in.</param>
/// <param name="OwedBy">Who undertook a commitment, as the conversation writes it, and <see langword="null" /> for every other aspect and for a commitment naming nobody.</param>
/// <param name="DueAt">When a commitment falls due, and <see langword="null" /> for every other aspect and for a commitment the conversation gave no date for.</param>
/// <param name="Sources">The messages the statement rests on, best first, spelled as the presentation plan spells a citation target.</param>
/// <remarks>
/// All of it is derived from somebody's mail and is personal data under the same rules as the mail itself, so none of
/// it reaches a log, a span attribute, or a telemetry event.
/// </remarks>
internal sealed record ClientMailThreadStateEntryResponse(
    string Aspect,
    string Text,
    string? OwedBy,
    DateTimeOffset? DueAt,
    IReadOnlyList<ClientCitationRequest> Sources)
{
    /// <summary>Describes one statement for the wire.</summary>
    /// <param name="entry">The statement the reader returned.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailThreadStateEntryResponse For(ThreadStateEntry entry) => new(
        entry.Aspect.ToString(),
        entry.Text,
        entry.OwedBy,
        entry.DueAt,
        [
            .. entry.Sources.Select(static source => new ClientCitationRequest(
                EmailCitationTarget.Kind,
                source.Value,
                null,
                null)),
        ]);
}
