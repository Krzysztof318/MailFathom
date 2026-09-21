// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
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

/// <summary>Asks one question of the user's mail, and reads the run answering it from wherever the reader left off.</summary>
/// <remarks>
/// <para>
/// Three routes rather than one, because a run outlives the request that starts it. The first asks the question and
/// answers with the run's identifier as soon as the question and its scope are known to be answerable; the second reads
/// the run, from its beginning or from a cursor; the third stops it. A single route that held the answer on the
/// connection that asked would lose the whole run when a phone changed network, which is exactly the case this surface
/// exists for — and would leave stopping indistinguishable from looking away.
/// </para>
/// <para>
/// <strong>Every one of them answers from any replica.</strong> The run's output is rows rather than objects in the
/// process composing it, so a client reading its own run reaches it wherever the load balancer routed the request, a
/// second screen the same person opened reaches the same answer, and a rolling upgrade does not lose it.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// is that decision, and the cursor this reading route takes is what makes it exact: a cursor addresses rows every
/// replica can read, so where a client lands decides nothing about what it is given.
/// </para>
/// <para>
/// <strong>A client is told to read again rather than handed the answer.</strong> Each write a run makes raises a
/// signal naming the run and the sequence it reached, over the hub every screen that person has open is already
/// listening to. It carries no part of the answer, which is what this route is for, so a deployment whose backplane is
/// somebody else's service never sees a line of mail — and a signal that never arrives costs nothing, the rows being
/// the guarantee.
/// </para>
/// <para>
/// The run reads mail and sends what it reads to a chat provider, so all three routes are published under the grant
/// that governs asking a question about mail anywhere else. The reading route is under the same one rather than the
/// reading grant, because what it hands back is the answer to a question that was asked under it.
/// </para>
/// <para>
/// Everything a run composes about the mail — a source, a quoted fragment, a subject — reaches the caller here and
/// nowhere else. None of it is logged, put on a span, or carried in a failure or a signal: the events that describe how
/// a run is going carry counts and closed values alone, which is what makes the run observable without any of the mail.
/// </para>
/// </remarks>
internal static class ClientDiscoveryRunEndpoints
{
    /// <summary>The route a question is asked at, relative to the client prefix.</summary>
    internal const string DiscoveryRunsRoute = "/discovery/runs";

    /// <summary>The route a run is read and stopped at, relative to the client prefix.</summary>
    /// <remarks>
    /// The run itself rather than a verb or a collection beneath it. Reading it is reading the run, from the beginning
    /// or from a cursor, and stopping it is asking that this run stop existing as work in progress — so one address
    /// carries both, and a second request naming the same run is answered the same way.
    /// </remarks>
    internal const string DiscoveryRunRoute = "/discovery/runs/{runId:guid}";

    /// <summary>The greatest size a question may have on the wire.</summary>
    /// <remarks>
    /// Generous against the question's own bound and against a scope naming as many accounts, folders, and messages as
    /// one may carry, and small enough that a body is refused before it is read rather than after.
    /// </remarks>
    internal const int MaxQuestionRequestBytes = 16 * 1024;

    /// <summary>Maps the routes into the client group, so they inherit the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientDiscoveryRuns(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost(DiscoveryRunsRoute, Start)
            .WithMetadata(new RequestSizeLimitAttribute(MaxQuestionRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapGet(DiscoveryRunRoute, Read)
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapDelete(DiscoveryRunRoute, Stop)
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Composes the address one run is read at.</summary>
    /// <param name="id">The run the address names.</param>
    /// <returns>The path, from the client prefix, that <c>202</c> points a client at.</returns>
    /// <remarks>
    /// Built out of the route the run is actually mapped at rather than written a second time, so the pattern and the
    /// address a client is handed cannot drift apart — a route renamed in one place and not the other would answer
    /// <c>202</c> pointing at nothing, and a client following the header would meet a <c>404</c> for a run that exists.
    /// </remarks>
    internal static string ReadAddressOf(DiscoveryRunId id) =>
        ClientEndpointOptions.RoutePrefix
        + DiscoveryRunRoute.Replace(
            "{runId:guid}",
            id.Value.ToString("D", CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>Starts a run over the question, or reports what was wrong with it.</summary>
    /// <param name="request">The question and the mail it may be answered from.</param>
    /// <param name="scopeResolver">Resolves which accounts and folders the answer may be drawn from, and names the acting user.</param>
    /// <param name="userClock">Reads the instant the acting person is standing on, which every relative period in the question is resolved against.</param>
    /// <param name="principals">Reports the principal the transport admitted, which the run executes under.</param>
    /// <param name="runs">Opens the run, which is where the deployment's bound on one person's concurrent runs is applied.</param>
    /// <param name="timeProvider">Stamps the run, which its retention and its ceiling are measured from.</param>
    /// <param name="launcher">Puts the run on a scope of its own and starts it.</param>
    /// <param name="cancellationToken">Cancels opening the run, which is the one part of this a caller waits for.</param>
    /// <returns><c>202</c> naming the run, <c>400</c> naming what was wrong with the question or its scope, <c>429</c> where this person is already running as many as they may, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// The question and the scope are validated here rather than in the run, so a mistake in either is a refusal a
    /// person can act on instead of a run that opens and immediately fails. Everything after that — whether this
    /// deployment answers questions at all, what a model made of the question, whether retrieval found anything — is the
    /// run's own and is written into it, because none of it is known while the request is still open.
    /// </para>
    /// <para>
    /// Junk is left out with no override, exactly as it is for asking a question through any other surface: content
    /// written to manipulate whoever reads it now has a model reading it, and somebody hunting a wrongly filed message
    /// uses the search that can ask for it.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Accepted<ClientDiscoveryRunResponse>, ProblemHttpResult>> Start(
        [FromBody] ClientDiscoveryRunRequest? request,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] MailUserClock userClock,
        [FromServices] IAuthorizedPrincipalSource principals,
        [FromServices] IDiscoveryRunStore runs,
        [FromServices] TimeProvider timeProvider,
        [FromServices] DiscoveryRunLauncher launcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(userClock);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(launcher);

        if (principals.Current is not { } caller)
        {
            return Refuse("A run is asked for by a caller this deployment admitted.");
        }

        MailQuestion question;
        try
        {
            question = QuestionOf(request, scopeResolver, userClock);
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this user is assigned.");
        }
        catch (MailboxQueryFilterInvalidException refusal)
        {
            return Refuse(refusal.Message);
        }
        catch (MailFolderRoleUnmappedException refusal)
        {
            return Refuse(refusal.Message);
        }
        catch (ArgumentException)
        {
            return Refuse("The account, the folder, the conversation, or a message names a value this deployment does not issue.");
        }

        var id = DiscoveryRunId.New();

        if (!await runs.TryOpenAsync(id, scopeResolver.User, timeProvider.GetUtcNow(), cancellationToken))
        {
            return TypedResults.Problem(
                $"You may run at most {DiscoveryRunBounds.MaximumConcurrentRunsPerUser} questions at once on this "
                + "deployment. Wait for one to finish.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        _ = launcher.Start(question, id, scopeResolver.User, caller);

        return TypedResults.Accepted(ReadAddressOf(id), new ClientDiscoveryRunResponse(id.Value));
    }

    /// <summary>Reads one run from wherever the caller left off, on whichever replica the request reached.</summary>
    /// <param name="runId">The run the caller is reading.</param>
    /// <param name="since">The last sequence the caller already holds, and absent to read the run from its beginning.</param>
    /// <param name="scopeResolver">Names the acting user, which is who may read the run.</param>
    /// <param name="runs">Where the deployment holds its runs, which every replica reads the same rows of.</param>
    /// <param name="timeProvider">Stamps the read, which the run's retention window is measured from.</param>
    /// <param name="cancellationToken">Cancels the read where the caller has gone away.</param>
    /// <returns>The run's standing and everything after that sequence, <c>404</c> where this user has no such run, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// <strong>One route serves the first read and every later one.</strong> A cursor left out means from the
    /// beginning, so a client opening a screen and a client told that the run advanced ask the same question. What
    /// comes back says whether the run is still working, which is what tells a client watching one to expect more —
    /// and a read that returns a run no longer running is what ends the watching, whatever the hub is doing.
    /// </para>
    /// <para>
    /// A cursor past what the run has reached, or below zero, reads as the beginning. Nobody can hold such a value
    /// honestly — a sequence is only ever learned by being sent it — so what it means is a cursor belonging to some
    /// other run, and replaying costs a client a few events it already has rather than an answer it never receives.
    /// </para>
    /// <para>
    /// A run belonging to somebody else is answered as no such run rather than as a refusal, so an identifier says
    /// nothing about whether it exists. A client that goes away ends its own read and nothing else, which is what lets
    /// it come back and be given what it missed — from any replica, the answer being rows rather than a buffer in the
    /// process that composed it.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientDiscoveryRunReadResponse>, NotFound>> Read(
        [FromRoute] Guid runId,
        [FromQuery] long? since,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] IDiscoveryRunStore runs,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (runId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var read = await runs.ReadAsync(
            DiscoveryRunId.Create(runId),
            scopeResolver.User,
            Math.Max(since ?? 0, 0),
            timeProvider.GetUtcNow(),
            cancellationToken);

        return read is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ClientDiscoveryRunReadResponse(read.Running, read.Events));
    }

    /// <summary>Stops one run, so it makes no further provider call and abandons the retrieval it is waiting on.</summary>
    /// <param name="runId">The run the caller is stopping.</param>
    /// <param name="scopeResolver">Names the acting user, which is who may stop the run.</param>
    /// <param name="runs">Where the deployment holds its runs, which is where the stop is recorded so it reaches whichever replica is executing.</param>
    /// <param name="executing">The runs this replica is executing, so a stop that landed here reaches the work at once.</param>
    /// <param name="timeProvider">Stamps the stop.</param>
    /// <param name="cancellationToken">Cancels recording the stop.</param>
    /// <returns><c>204</c> where the run was this user's and is now stopping, <c>404</c> where the deployment holds no such run for this user, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Stopping stops the spending rather than the watching.</strong> Ending a read says nothing about the run,
    /// which is addressed by an identifier and outlives every request it is reached over — so a control that only
    /// stopped reading would cost exactly what not stopping costs. This reaches the provider call and the retrieval.
    /// </para>
    /// <para>
    /// <strong>It works from any replica, and it is recorded rather than acted on.</strong> The stop is written against
    /// the run, which is what the executing replica meets as the first write the store refuses; that replica then ends
    /// the run itself, so the ending carries the counts its own ledger holds rather than a cost figure of nothing
    /// composed by whichever replica the request happened to reach. Asking this replica as well is what makes a stop
    /// immediate where it does hold the run rather than a provider call late.
    /// </para>
    /// <para>
    /// What the run had already written stays written, and a client reading it is given the blocks that arrived and
    /// then a <c>failed</c> event naming the stop. What the run had already spent stays spent, in the run's own counts
    /// and in the period's: stopping buys the remainder rather than a refund.
    /// </para>
    /// <para>
    /// A run that has already ended is stopped successfully and nothing happens, because whoever asked could not have
    /// known it finished a moment earlier. A run belonging to somebody else is answered as no such run, exactly as
    /// reading one is, so an identifier says nothing about whether it exists.
    /// </para>
    /// </remarks>
    internal static async Task<Results<NoContent, NotFound>> Stop(
        [FromRoute] Guid runId,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] IDiscoveryRunStore runs,
        [FromServices] ExecutingDiscoveryRuns executing,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(executing);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (runId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var id = DiscoveryRunId.Create(runId);

        if (!await runs.TryRequestStopAsync(id, scopeResolver.User, timeProvider.GetUtcNow(), cancellationToken))
        {
            return TypedResults.NotFound();
        }

        executing.TryRequestStop(id, scopeResolver.User);

        return TypedResults.NoContent();
    }

    /// <summary>Reads the question and the mail it may be answered from, refusing anything the request got wrong.</summary>
    private static MailQuestion QuestionOf(
        ClientDiscoveryRunRequest? request,
        MailboxScopeResolver scopeResolver,
        MailUserClock userClock)
    {
        var text = MailQuestionText.Create(request?.Question);

        var scope = scopeResolver.ReadableScope(
            Selectors(request?.Accounts),
            Folders(request?.Folders),
            JunkMailInclusion.Excluded);

        return new MailQuestion(text, NarrowedTo(scope, request), userClock.Now());
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
/// <param name="Accounts">The accounts to read, by identifier or display name, and empty for every account the user is assigned.</param>
/// <param name="Folders">The folders to read, by alias or as <c>role:Inbox</c>, and empty for every folder.</param>
/// <param name="Thread">The conversation the question was asked about, or <see langword="null" /> where it was asked about none.</param>
/// <param name="Emails">The individual messages the question was asked about, and empty where it was asked about none.</param>
/// <remarks>
/// It names no user. The acting user comes off the credential, which is what makes a question about somebody else's
/// mail something a caller cannot express here rather than something the surface has to refuse.
/// </remarks>
internal sealed record ClientDiscoveryRunRequest(
    string? Question,
    IReadOnlyList<string>? Accounts,
    IReadOnlyList<string>? Folders,
    Guid? Thread,
    IReadOnlyList<Guid>? Emails);

/// <summary>The run a question started.</summary>
/// <param name="RunId">The run, which is what it is read at and what a client that lost its connection comes back by.</param>
/// <remarks>
/// It carries nothing about the answer, because there is none yet: the request returns as soon as the question is known
/// to be answerable, which is the point at which somebody can be shown that their question is running.
/// </remarks>
internal sealed record ClientDiscoveryRunResponse(Guid RunId);

/// <summary>One read of a run: where it stands, and everything it has written past the cursor the reader held.</summary>
/// <param name="Running">Whether the run is still executing, which is what tells a client watching it to expect more.</param>
/// <param name="Events">Everything after the stated cursor, in sequence order, and empty where the reader was already caught up.</param>
/// <remarks>
/// A client holds the last sequence it read and sends it back as the cursor, so a burst of advances costs one read:
/// what comes back is everything after that point rather than one event per announcement. Nothing here says how far the
/// run has got beyond what the last event carries, because a reader that was already caught up has not moved and one
/// that was not holds the number on the event it just read.
/// </remarks>
internal sealed record ClientDiscoveryRunReadResponse(bool Running, IReadOnlyList<DiscoveryRunEvent> Events);
