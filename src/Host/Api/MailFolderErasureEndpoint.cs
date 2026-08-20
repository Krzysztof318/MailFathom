// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the one operation in MailFathom that takes a folder's local mail away.</summary>
/// <remarks>
/// <para>
/// Nothing else erases a mirror. Switching a folder's synchronization off keeps what it stored, and a mapping removed
/// from configuration leaves the rows where they are, both so that a configuration value cannot dispose of somebody's
/// mail. That leaves an operator who genuinely wants the storage back with nothing to ask, and this is the ask.
/// </para>
/// <para>
/// It is here rather than on the MCP surface for the reason every administrative route is: erasing a mailbox's worth of
/// mail is not something a model reasons over, and what bounds administrative access is what should bound it.
/// <strong>It is published under <c>mailfathom.admin.erase</c></strong>, the grant this deployment allocates to
/// disposing of what it holds — this route and the erasure of one person from the contact book — so a credential that
/// reads, operates, or spends does not reach it.
/// </para>
/// <para>
/// One request is one bounded pass, and the command repeats it. The bound is the one the backward pass over stored mail
/// already carries, so the transaction that removes the rows is the size reconciliation has always committed; answering
/// after each pass is what makes an interrupted erasure resumable rather than a half-erased folder nothing can finish.
/// </para>
/// </remarks>
internal static class MailFolderErasureEndpoint
{
    /// <summary>The route one bounded pass of an erasure is asked for on, relative to the administrative prefix.</summary>
    internal const string ErasureRoute = "/folders/erasure";

    /// <summary>The greatest request body the route reads before refusing it.</summary>
    /// <remarks>
    /// The body names one account and one folder, so a few hundred bytes is the whole of anything it could mean. Stated
    /// for the reason the rule run states it: the server's own default is measured in tens of megabytes, which here
    /// would let an authenticated client make the process buffer a body four orders of magnitude larger than the
    /// request it is sending.
    /// </remarks>
    internal const int MaxErasureRequestBytes = 4 * 1024;

    /// <summary>Maps the erasure route into the administrative group, so it inherits its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailFolderErasure(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the rule run route reaches
        // it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature,
        // so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(ErasureRoute, EraseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxErasureRequestBytes))
            .RequirePermission(MailFathomPermission.AdminErase);
    }

    /// <summary>Erases one bounded pass of what is stored for a folder this deployment no longer mirrors.</summary>
    /// <param name="request">The account and the folder whose stored mail is erased.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="mappings">Reports what the account's configuration says about the named folder, where it says anything.</param>
    /// <param name="eraser">Performs the pass.</param>
    /// <param name="cancellationToken">Cancels the pass when the client disconnects, leaving what earlier passes committed.</param>
    /// <returns><c>200</c> with what the pass erased, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A folder the account still mirrors is refused rather than erased. Removing the rows of a folder a run is about to
    /// visit would open a hole the next run silently refills, so what the caller would have got is the cost of a
    /// remirror and none of the storage back — which is a refusal that names the two ways to make the folder erasable
    /// rather than an operation to perform carefully.
    /// </remarks>
    internal static async Task<Results<Ok<MailFolderErasureResponse>, ProblemHttpResult>> EraseAsync(
        [FromBody] MailFolderErasureRequest? request,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] IMailFolderMappingReader mappings,
        [FromServices] UnmirroredMailFolderEraser eraser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(eraser);

        if (AdminAccountRequest.Resolve(request?.Account, accounts) is not { } accountId)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        if (!MailFolderAlias.TryCreate(request?.Folder, out var folderAlias))
        {
            return TypedResults.Problem(
                "The request named no folder. Name the alias of the folder whose stored mail is to be erased.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (mappings.FindFolderNamed(accountId, folderAlias) is { Participation.IsSynchronized: true })
        {
            return TypedResults.Problem(
                $"The folder '{folderAlias.Value}' is still mirrored, so erasing it would only cost a remirror. Switch its Synchronize off, or remove its mapping, and ask again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var erasure = await eraser.EraseAsync(accountId, folderAlias, cancellationToken);

        return TypedResults.Ok(new MailFolderErasureResponse(
            accountId.Value,
            folderAlias.Value,
            erasure.ErasedEmailCount,
            erasure.EmailsRemain));
    }
}

/// <summary>What a deployment is asked when a folder's stored mail is to be erased.</summary>
/// <param name="Account">The account the folder belongs to, as the deployment's configuration names it.</param>
/// <param name="Folder">MailFathom's own alias for the folder, which need not be one any mapping still names.</param>
internal sealed record MailFolderErasureRequest(string? Account, string? Folder);

/// <summary>What one bounded pass of an erasure removed, and whether the folder still holds stored mail.</summary>
/// <param name="Account">The account the folder belongs to.</param>
/// <param name="Folder">The normalized alias the pass ran against.</param>
/// <param name="ErasedEmailCount">How many stored emails this pass removed, with everything declared from them.</param>
/// <param name="EmailsRemain">Whether the folder still holds mail a further pass would reach.</param>
/// <remarks>
/// Counts and MailFathom's own names for things, and nothing a mailbox supplied. What was erased is a number rather
/// than a list, because a list of what a deployment has just disposed of would be a copy of the part of the mailbox the
/// operator asked it to stop keeping.
/// </remarks>
internal sealed record MailFolderErasureResponse(
    string Account,
    string Folder,
    int ErasedEmailCount,
    bool EmailsRemain);
