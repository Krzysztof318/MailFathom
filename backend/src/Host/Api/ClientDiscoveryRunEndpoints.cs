// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Asks one question of the owner's mail and streams the run answering it.</summary>
/// <remarks>
/// <para>
/// Two routes rather than one, because a run outlives the request that starts it. The first asks the question and
/// answers with the run's identifier as soon as the question and its scope are known to be answerable; the second reads
/// the run, and reads it again after a dropped connection. A single route that streamed the answer over the connection
/// that asked would lose the whole run when a phone changed network, which is exactly the case this surface exists for.
/// </para>
/// <para>
/// <strong>The transport is Server-Sent Events, and the contract is the events rather than the transport.</strong>
/// The reasoning is recorded on the feature page: a run is one-directional, its events are small JSON documents, and
/// the resumption a dropped connection needs is already in the protocol as <c>Last-Event-ID</c> — so what a plain
/// chunked response would need built by hand is what this gets from the browser's own <c>EventSource</c>. SignalR would
/// carry it too and is not introduced for it: what it adds is multi-directional messaging and a second connection
/// lifecycle, neither of which a run has any use for. Nothing about the events depends on the choice, so a deployment
/// that ever needed a different transport would serve the same sequence over it.
/// </para>
/// <para>
/// The run reads mail and sends what it reads to a chat provider, so both routes are published under the grant that
/// governs asking a question about mail anywhere else. The reading route is under the same one rather than the reading
/// grant, because what it hands back is the answer to a question that was asked under it.
/// </para>
/// <para>
/// Everything a run publishes about the mail — a source, a quoted fragment, a subject — reaches the caller here and
/// nowhere else. None of it is logged, put on a span, or carried in a failure: the events that describe how a run is
/// going carry counts and closed values alone, which is what makes the run observable without any of the mail.
/// </para>
/// </remarks>
internal static class ClientDiscoveryRunEndpoints
{
    /// <summary>The route a question is asked at, relative to the client prefix.</summary>
    internal const string DiscoveryRunsRoute = "/discovery/runs";

    /// <summary>The route a run is read at, relative to the client prefix.</summary>
    internal const string DiscoveryRunEventsRoute = "/discovery/runs/{runId:guid}/events";

    /// <summary>The greatest size a question may have on the wire.</summary>
    /// <remarks>
    /// Generous against the question's own bound and against a scope naming as many accounts, folders, and messages as
    /// one may carry, and small enough that a body is refused before it is read rather than after.
    /// </remarks>
    internal const int MaxQuestionRequestBytes = 16 * 1024;

    /// <summary>The header a client reattaching to a run states its place in it with.</summary>
    /// <remarks>The protocol's own, which a browser's <c>EventSource</c> sends by itself, so resuming needs no code in a client that did not write the reconnection either.</remarks>
    private const string LastEventIdHeader = "Last-Event-ID";

    /// <summary>Maps the routes into the client group, so they inherit the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientDiscoveryRuns(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost(DiscoveryRunsRoute, Start)
            .WithMetadata(new RequestSizeLimitAttribute(MaxQuestionRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapGet(DiscoveryRunEventsRoute, Watch)
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Composes the address one run's events are read at.</summary>
    /// <param name="id">The run the address names.</param>
    /// <returns>The path, from the client prefix, that <c>202</c> points a client at.</returns>
    /// <remarks>
    /// Built out of the route the run is actually mapped at rather than written a second time, so the pattern and the
    /// address a client is handed cannot drift apart — a route renamed in one place and not the other would answer
    /// <c>202</c> pointing at nothing, and a client following the header would meet a <c>404</c> for a run that exists.
    /// </remarks>
    internal static string EventsAddressOf(DiscoveryRunId id) =>
        ClientEndpointOptions.RoutePrefix
        + DiscoveryRunEventsRoute.Replace(
            "{runId:guid}",
            id.Value.ToString("D", CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>Starts a run over the question, or reports what was wrong with it.</summary>
    /// <param name="request">The question and the mail it may be answered from.</param>
    /// <param name="scopeResolver">Resolves which accounts and folders the answer may be drawn from, and names the acting owner.</param>
    /// <param name="principals">Reports the principal the transport admitted, which the run executes under.</param>
    /// <param name="registry">Holds the run while it executes and while a client can still come back for it.</param>
    /// <param name="launcher">Puts the run on a scope of its own and starts it.</param>
    /// <returns><c>202</c> naming the run, <c>400</c> naming what was wrong with the question or its scope, <c>429</c> where this process is already running as many as it may, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// The question and the scope are validated here rather than in the run, so a mistake in either is a refusal a
    /// person can act on instead of a stream that opens and immediately fails. Everything after that — whether this
    /// deployment answers questions at all, what a model made of the question, whether retrieval found anything — is the
    /// run's own and is published to the stream, because none of it is known while the request is still open.
    /// </para>
    /// <para>
    /// Junk is left out with no override, exactly as it is for asking a question through any other surface: content
    /// written to manipulate whoever reads it now has a model reading it, and somebody hunting a wrongly filed message
    /// uses the search that can ask for it.
    /// </para>
    /// </remarks>
    internal static Results<Accepted<ClientDiscoveryRunResponse>, ProblemHttpResult> Start(
        [FromBody] ClientDiscoveryRunRequest? request,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] IAuthorizedPrincipalSource principals,
        [FromServices] DiscoveryRunRegistry registry,
        [FromServices] DiscoveryRunLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(launcher);

        if (principals.Current is not { } caller)
        {
            return Refuse("A run is asked for by a caller this deployment admitted.");
        }

        MailQuestion question;
        try
        {
            question = QuestionOf(request, scopeResolver);
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this owner owns.");
        }
        catch (MailboxQueryFilterInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
        catch (ArgumentException)
        {
            return Refuse("The account, the folder, the conversation, or a message names a value this deployment does not issue.");
        }

        if (!registry.TryOpen(scopeResolver.Owner, out var journal))
        {
            return TypedResults.Problem(
                $"This deployment runs at most {DiscoveryRunBounds.MaximumConcurrentRuns} questions at once. Wait for one to finish.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        _ = launcher.Start(question, journal, caller);

        return TypedResults.Accepted(
            EventsAddressOf(journal.Id),
            new ClientDiscoveryRunResponse(journal.Id.Value));
    }

    /// <summary>Streams one run from wherever the caller left off.</summary>
    /// <param name="runId">The run the caller is reading.</param>
    /// <param name="context">The request, which is read for the place a reconnecting client states it left off at.</param>
    /// <param name="scopeResolver">Names the acting owner, which is who may read the run.</param>
    /// <param name="registry">Holds the runs this process is executing or has not yet forgotten.</param>
    /// <returns>The events as a Server-Sent Events stream, <c>404</c> where this process holds no such run for this owner, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A run belonging to somebody else is answered as no such run rather than as a refusal, so an identifier says
    /// nothing about whether it exists. The stream ends when the run does; a client that disconnects ends its own read
    /// and nothing else, which is what lets it come back and be given what it missed.
    /// </remarks>
    internal static Results<ServerSentEventsResult<DiscoveryRunEvent>, NotFound> Watch(
        [FromRoute] Guid runId,
        HttpContext context,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] DiscoveryRunRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(registry);

        if (runId == Guid.Empty
            || !registry.TryFind(DiscoveryRunId.Create(runId), scopeResolver.Owner, out var journal))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.ServerSentEvents(
            Published(journal, ResumedFrom(context), context.RequestAborted));
    }

    /// <summary>Reads what a run publishes into the items the protocol carries.</summary>
    /// <remarks>
    /// The identifier is the event's own sequence, which is what a reconnecting client sends back, and the type is the
    /// same word the JSON discriminator carries so a client may branch on either.
    /// </remarks>
    private static async IAsyncEnumerable<SseItem<DiscoveryRunEvent>> Published(
        DiscoveryRunJournal journal,
        long resumedFrom,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var published in journal.ReadFromAsync(resumedFrom, cancellationToken))
        {
            yield return new SseItem<DiscoveryRunEvent>(published, published.EventName)
            {
                EventId = published.Sequence.ToString(CultureInfo.InvariantCulture),
            };
        }
    }

    /// <summary>Reads the place a reconnecting client says it left off at.</summary>
    /// <remarks>
    /// A header that is absent, blank, or not a sequence this run could have issued reads as the beginning, which is
    /// what a client connecting for the first time means. That is the safe direction: a run is bounded and complete, so
    /// replaying it costs a client a few events it already has rather than an answer it never receives.
    /// </remarks>
    private static long ResumedFrom(HttpContext context) =>
        long.TryParse(
            context.Request.Headers[LastEventIdHeader],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var sequence)
            ? sequence
            : 0;

    /// <summary>Reads the question and the mail it may be answered from, refusing anything the request got wrong.</summary>
    private static MailQuestion QuestionOf(ClientDiscoveryRunRequest? request, MailboxScopeResolver scopeResolver)
    {
        var text = MailQuestionText.Create(request?.Question);

        var scope = scopeResolver.ReadableScope(
            Selectors(request?.Accounts),
            Folders(request?.Folders),
            JunkMailInclusion.Excluded);

        return new MailQuestion(text, NarrowedTo(scope, request));
    }

    /// <summary>Narrows a resolved scope to what the question was actually asked about.</summary>
    /// <remarks>
    /// A conversation or a handful of selected messages is what a question asked from a mail screen is about, and the
    /// narrowing belongs to the scope rather than to a filter applied afterwards: it travels into the query, so a
    /// question about four messages reads four rows instead of ranking the mailbox and discarding the rest.
    /// </remarks>
    private static MailboxScope NarrowedTo(MailboxScope scope, ClientDiscoveryRunRequest? request)
    {
        var narrowed = request?.Thread is { } thread && thread != Guid.Empty
            ? scope.NarrowedToThread(EmailThreadId.Create(thread))
            : scope;

        return request?.Emails is { Count: > 0 } emails
            ? narrowed.NarrowedToEmails([.. emails.Select(StoredEmailId.Create)])
            : narrowed;
    }

    private static IReadOnlyList<MailAccountSelector> Selectors(IReadOnlyList<string>? accounts) =>
        accounts is null ? [] : [.. accounts.Select(MailAccountSelector.Create)];

    private static IReadOnlyList<MailFolderReference> Folders(IReadOnlyList<string>? folders) =>
        folders is null ? [] : [.. folders.Select(MailFolderReference.Create)];

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    /// <remarks>Without echoing it because a question is the most revealing value this surface carries, and a problem detail is the one part of a response that reaches a log by default.</remarks>
    private static Results<Accepted<ClientDiscoveryRunResponse>, ProblemHttpResult> Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The question one run is asked, and the mail it may be answered from.</summary>
/// <param name="Question">What the caller wants to know.</param>
/// <param name="Accounts">The accounts to read, by identifier or display name, and empty for every account the owner owns.</param>
/// <param name="Folders">The folders to read, by alias or as <c>role:Inbox</c>, and empty for every folder.</param>
/// <param name="Thread">The conversation the question was asked about, or <see langword="null" /> where it was asked about none.</param>
/// <param name="Emails">The individual messages the question was asked about, and empty where it was asked about none.</param>
/// <remarks>
/// It names no owner. The acting owner comes off the credential, which is what makes a question about somebody else's
/// mail something a caller cannot express here rather than something the surface has to refuse.
/// </remarks>
internal sealed record ClientDiscoveryRunRequest(
    string? Question,
    IReadOnlyList<string>? Accounts,
    IReadOnlyList<string>? Folders,
    Guid? Thread,
    IReadOnlyList<Guid>? Emails);

/// <summary>The run a question started.</summary>
/// <param name="RunId">The run, which is what its events are read at and what a dropped connection reattaches by.</param>
/// <remarks>
/// It carries nothing about the answer, because there is none yet: the request returns as soon as the question is known
/// to be answerable, which is the point at which somebody can be shown that their question is running.
/// </remarks>
internal sealed record ClientDiscoveryRunResponse(Guid RunId);
