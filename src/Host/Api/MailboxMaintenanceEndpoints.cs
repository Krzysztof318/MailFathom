// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
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
/// rewritten. One request is one bounded pass and the command repeats it, which is what makes an interrupted
/// re-derivation resumable rather than a scope in a state nothing can finish.
/// </para>
/// <para>
/// Neither touches embeddings. Chunks and vectors stay the embedding group's business, and a refresh must not quietly
/// spend the provider budget an operator has not asked to spend.
/// </para>
/// <para>
/// Both are here rather than on the MCP surface for the reason every administrative route is: re-reading a mailbox is
/// not something a model reasons over, and what bounds administrative access is what should bound it. These routes
/// postdate ADR 0012's table and are allocated under its rule: assessing a rewind reports what the deployment holds and
/// is <c>mailfathom.admin.read</c>, while performing either of them asks the deployment to do work it can already do and
/// is <c>mailfathom.admin.operate</c>.
/// </para>
/// </remarks>
internal static class MailboxMaintenanceEndpoints
{
    /// <summary>The route a rewind is assessed and performed on, relative to the administrative prefix.</summary>
    internal const string RewindRoute = "/mailbox/rewind";

    /// <summary>The route one bounded pass of a re-derivation is asked for on, relative to the administrative prefix.</summary>
    internal const string RederivationRoute = "/mailbox/rederivation";

    /// <summary>The greatest request body either write reads before refusing it.</summary>
    /// <remarks>
    /// The body names one account and one folder, so a few hundred bytes is the whole of anything it could mean. Stated
    /// for the reason the erasure route states it: the server's own default is measured in tens of megabytes, which
    /// here would let an authenticated client make the process buffer a body four orders of magnitude larger than the
    /// request it is sending.
    /// </remarks>
    internal const int MaxMaintenanceRequestBytes = 4 * 1024;

    /// <summary>Maps both maintenance routes into the administrative group, so they inherit its authorization.</summary>
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
        [FromServices] IMailAccountCatalog accounts,
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
        [FromServices] IMailAccountCatalog accounts,
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

    /// <summary>Runs one bounded pass of the re-derivation over one scope's stored mail.</summary>
    /// <param name="request">The account, and the one folder of it, whose stored mail is re-read.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="rederivation">Performs the pass.</param>
    /// <param name="cancellationToken">Cancels the pass when the client disconnects, leaving what earlier batches committed.</param>
    /// <returns><c>200</c> with what the pass re-derived, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<MailboxRederivationResponse>, ProblemHttpResult>> RederiveAsync(
        [FromBody] MailboxMaintenanceRequest? request,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] StoredMailRederivation rederivation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(rederivation);

        if (ResolveScope(request?.Account, request?.Folder, accounts) is not { } resolution)
        {
            return Refusal(request?.Account, accounts);
        }

        var pass = await rederivation.RunAsync(resolution, cancellationToken);

        return TypedResults.Ok(new MailboxRederivationResponse(
            resolution.Account.Value,
            resolution.Folder?.Value,
            pass.RederivedEmailCount,
            pass.UnreadableEmailCount,
            pass.MissingContentEmailCount,
            pass.EmailsRemain));
    }

    /// <summary>Reads the scope a caller named, or nothing when either half of it is not one this deployment serves.</summary>
    /// <remarks>
    /// An absent folder is the whole account rather than a refusal, because that is the shape both commands take when
    /// an operator names none. Blank text is treated as absent for the same reason a query string omitting the
    /// parameter is: the two are indistinguishable to a caller writing a URL by hand.
    /// </remarks>
    private static StoredMailScope? ResolveScope(string? account, string? folder, IMailAccountCatalog accounts)
    {
        if (ResolveAccount(account, accounts) is not { } accountId)
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

    /// <summary>Reads the account a request named, or nothing when this deployment does not serve it.</summary>
    private static MailAccountId? ResolveAccount(string? account, IMailAccountCatalog accounts)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var accountId = MailAccountId.Create(account);

        return accounts.ServedAccounts.Any(served => served.Id == accountId) ? accountId : null;
    }

    /// <summary>States which half of the scope was wrong, without echoing an empty one.</summary>
    /// <remarks>
    /// The folder is not a parameter because it is not read: a scope that failed to resolve with an account this
    /// deployment serves failed on its folder, and naming the text back would echo whatever a caller sent.
    /// </remarks>
    private static ProblemHttpResult Refusal(string? account, IMailAccountCatalog accounts)
    {
        if (ResolveAccount(account, accounts) is not null)
        {
            return TypedResults.Problem(
                "The request named a folder that is not an alias. Name the alias of one folder, or name none at all to cover every folder the account holds mail in.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Problem(
            string.IsNullOrWhiteSpace(account)
                ? "The request named no mail account."
                : $"This deployment configures no mail account named '{account}'.",
            statusCode: StatusCodes.Status400BadRequest);
    }
}
