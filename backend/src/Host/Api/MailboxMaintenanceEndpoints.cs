// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the two ways stored mail is brought up to the properties a newer release records.</summary>
/// <remarks>
/// <para>
/// A release adds properties to stored mail, and every message stored before it carries none of them. Nothing in a
/// running deployment fills them in: the forward pass asks a server only about UIDs above the folder's checkpoint and
/// the backward pass reconciles what disappeared, so mail already mirrored keeps whatever shape it had on the day it
/// arrived. These two routes are what an operator asks about that, and they are two rather than one because the
/// properties have two sources and very different costs.
/// </para>
/// <para>
/// <strong>The rewind</strong> discards the account's durable progress so the next runs read its folders from the first
/// UID inside the account's window. Everything the server knows is then re-read, which is also its cost: the whole
/// scope off the wire, back through MIME extraction, and back into the content store. It is therefore read before it is
/// performed — one path with <c>GET</c> answering what the scope holds and <c>POST</c> performing it — for the reason
/// <see cref="EmbeddingProfileEndpoints" /> reads an estimate before an activation: the figure an operator agrees to
/// and the figure the deployment acts on have to be one figure.
/// </para>
/// <para>
/// <strong>The re-derivation</strong> reads the raw MIME this deployment already stores, runs it back through the MIME
/// reader, and writes the row's own columns. No mailbox session is opened, nothing is fetched, and no content is
/// rewritten. One path again, with <c>POST</c> asking for the walk and <c>GET</c> reporting where it got to: the
/// deployment carries it as durable background work, so the request returns as soon as the run is written down and an
/// operator comes back to the same place to find out what has come of it.
/// </para>
/// <para>
/// Neither touches embeddings. Chunks and vectors stay the embedding group's business, and a refresh must not quietly
/// spend the provider budget an operator has not asked to spend.
/// </para>
/// <para>
/// Both are here rather than on the MCP surface for the reason every administrative route is: re-reading a mailbox is
/// not something a model reasons over, and what bounds administrative access is what should bound it. These routes
/// postdate ADR 0012's table and are allocated under its rule: assessing a rewind and reading where a re-derivation got
/// to both report what the deployment holds and are <c>mailfathom.admin.read</c>, while asking for either of them is
/// asking the deployment to do work it can already do and is <c>mailfathom.admin.operate</c>.
/// </para>
/// </remarks>
internal static class MailboxMaintenanceEndpoints
{
    /// <summary>The route a rewind is assessed and performed on, relative to the administrative prefix.</summary>
    internal const string RewindRoute = "/mailbox/rewind";

    /// <summary>The route a re-derivation is asked for and read from, relative to the administrative prefix.</summary>
    /// <remarks>
    /// One path and two verbs, because they are one operation asked for and then watched: what the reading reports is
    /// the run the write asked for, and an operator who started a walk of their mailbox comes back to the same place to
    /// find out where it got to.
    /// </remarks>
    internal const string RederivationRoute = "/mailbox/rederivation";

    /// <summary>The greatest request body either write reads before refusing it.</summary>
    /// <remarks>
    /// The body names one account and one folder, so a few hundred bytes is the whole of anything it could mean. Stated
    /// for the reason the erasure route states it: the server's own default is measured in tens of megabytes, which
    /// here would let an authenticated client make the process buffer a body four orders of magnitude larger than the
    /// request it is sending.
    /// </remarks>
    internal const int MaxMaintenanceRequestBytes = 4 * 1024;

    /// <summary>Maps the maintenance routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailboxMaintenance(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(RewindRoute, AssessRewindAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the erasure route reaches
        // it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature,
        // so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(RewindRoute, RewindAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxMaintenanceRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapPost(RederivationRoute, RederiveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxMaintenanceRequestBytes))
            .RequirePermission(MailFathomPermission.AdminOperate);

        api.MapGet(RederivationRoute, ReadRederivationAsync)
            .RequirePermission(MailFathomPermission.AdminRead);
    }

    /// <summary>Reports what a rewind of one scope would have the next runs read again, without discarding anything.</summary>
    /// <param name="account">The account the rewind would cover, as the deployment's configuration names it.</param>
    /// <param name="folder">The one folder of it to cover, or nothing for every folder the account holds mail in.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="rewind">Counts what the scope holds.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the count, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<MailboxRewindAssessmentResponse>, ProblemHttpResult>> AssessRewindAsync(
        [FromQuery] string? account,
        [FromQuery] string? folder,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailSynchronizationRewind rewind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(rewind);

        if (ResolveScope(account, folder, accounts) is not { } resolution)
        {
            return Refusal(account, accounts);
        }

        var storedEmailCount = await rewind.AssessAsync(resolution, cancellationToken);

        return TypedResults.Ok(new MailboxRewindAssessmentResponse(
            resolution.Account.Value,
            resolution.Folder?.Value,
            storedEmailCount));
    }

    /// <summary>Discards the durable synchronization progress of one scope's folder bindings.</summary>
    /// <param name="request">The account, and the one folder of it, whose progress is discarded.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="rewind">Performs the removal.</param>
    /// <param name="cancellationToken">Cancels the removal when the client disconnects, before its single transaction commits.</param>
    /// <returns><c>200</c> with the folders that held progress, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A scope whose bindings hold no progress at all succeeds having discarded nothing, which is what an account that
    /// has never synchronized answers and is not a refusal. A run that is in flight loses the race safely rather than
    /// being coordinated with: it decided from progress that no longer exists, so its own advance is refused by the
    /// checkpoint's compare-and-set contract instead of being written over a folder this removal has rewound.
    /// </remarks>
    internal static async Task<Results<Ok<MailboxRewindResponse>, ProblemHttpResult>> RewindAsync(
        [FromBody] MailboxMaintenanceRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailSynchronizationRewind rewind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(rewind);

        if (ResolveScope(request?.Account, request?.Folder, accounts) is not { } resolution)
        {
            return Refusal(request?.Account, accounts);
        }

        var rewound = await rewind.RewindAsync(resolution, cancellationToken);

        return TypedResults.Ok(new MailboxRewindResponse(
            resolution.Account.Value,
            resolution.Folder?.Value,
            [.. rewound.Select(alias => alias.Value)]));
    }

    /// <summary>Asks for one scope's stored mail to be re-read, and answers with the run that will carry it.</summary>
    /// <param name="request">The account, and the one folder of it, whose stored mail is re-read.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="requests">Records the run, or reports the one already in front of the scope.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the run, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// It records that the run is wanted and re-reads nothing. The walk is durable background work, so the request
    /// neither performs it nor keeps it alive — which is what stops an operator's terminal closing from cancelling a
    /// walk of their mailbox, and what makes this answer immediately however large the mailbox is.
    /// <para>
    /// A second request while one is outstanding is answered with the run already under way rather than refused, and it
    /// starts no second walk. The answer says which of the two happened, and what is carrying the run.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<MailboxRederivationStartResponse>, ProblemHttpResult>> RederiveAsync(
        [FromBody] MailboxMaintenanceRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] StoredMailRederivationRequests requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(requests);

        if (ResolveScope(request?.Account, request?.Folder, accounts) is not { } resolution)
        {
            return Refusal(request?.Account, accounts);
        }

        var submitted = await requests.SubmitAsync(resolution, cancellationToken);

        return TypedResults.Ok(MailboxRederivationStartResponse.For(submitted));
    }

    /// <summary>Reports where one scope's re-derivation has got to, or how the last one ended.</summary>
    /// <param name="account">The account whose run is read, as the deployment's configuration names it.</param>
    /// <param name="folder">The one folder of it the run covers, or nothing for the account's own run.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="runs">Reads the one run a scope may have, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the run, <c>200</c> with none where the scope has never been asked for one, or <c>400</c>.</returns>
    /// <remarks>
    /// The scope is named exactly as the request that started the run named it, because two scopes are two runs: a walk
    /// of one folder is not what an operator asking about the whole account is waiting on, and answering with it would
    /// report progress over a mailbox nobody is walking.
    /// </remarks>
    internal static async Task<Results<Ok<MailboxRederivationStateResponse>, ProblemHttpResult>> ReadRederivationAsync(
        [FromQuery] string? account,
        [FromQuery] string? folder,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] StoredMailRederivationRunReader runs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(runs);

        if (ResolveScope(account, folder, accounts) is not { } resolution)
        {
            return Refusal(account, accounts);
        }

        var run = await runs.FindAsync(resolution, cancellationToken);

        return TypedResults.Ok(new MailboxRederivationStateResponse(
            resolution.Account.Value,
            resolution.Folder?.Value,
            run is null ? null : MailboxRederivationRunResponse.For(run)));
    }

    /// <summary>Reads the scope a caller named, or nothing when either half of it is not one this deployment serves.</summary>
    /// <remarks>
    /// An absent folder is the whole account rather than a refusal, because that is the shape both commands take when
    /// an operator names none. Blank text is treated as absent for the same reason a query string omitting the
    /// parameter is: the two are indistinguishable to a caller writing a URL by hand.
    /// </remarks>
    private static StoredMailScope? ResolveScope(string? account, string? folder, IDeploymentMailAccountCatalog accounts)
    {
        if (AdminAccountRequest.Resolve(account, accounts) is not { } accountId)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return new StoredMailScope(accountId, null);
        }

        return MailFolderAlias.TryCreate(folder, out var folderAlias)
            ? new StoredMailScope(accountId, folderAlias)
            : null;
    }

    /// <summary>States which half of the scope was wrong, without echoing an empty one.</summary>
    /// <remarks>
    /// The folder is not a parameter because it is not read: a scope that failed to resolve with an account this
    /// deployment serves failed on its folder, and naming the text back would echo whatever a caller sent.
    /// </remarks>
    private static ProblemHttpResult Refusal(string? account, IDeploymentMailAccountCatalog accounts)
    {
        if (AdminAccountRequest.Resolve(account, accounts) is not null)
        {
            return TypedResults.Problem(
                "The request named a folder that is not an alias. Name the alias of one folder, or name none at all to cover every folder the account holds mail in.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return AdminAccountRequest.Refuse(account);
    }
}
