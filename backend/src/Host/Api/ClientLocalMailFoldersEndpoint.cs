// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Folders.Local;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the folders MailFathom keeps for an account whose mailbox it holds, and takes the user's edits to them.</summary>
/// <remarks>
/// <para>
/// These are local folders, not the source server's: every write is refused on an account whose mailbox MailFathom does
/// not hold, and no route here reaches a mail server. The folders of a mirrored account are what
/// <see cref="ClientMailFoldersEndpoint" /> serves.
/// </para>
/// <para>
/// A folder is named by its identity rather than by its path, so a rename or a move never invalidates a reference a
/// client already holds. Each write is a route of its own, posted, so the verb and the path say which act a request is
/// and a body can never be read as a different one.
/// </para>
/// <para>
/// A refusal carries the refusal's own name as <c>refusal</c> beside its detail, so a client can draw the right message
/// without parsing prose: <c>404</c> for an account, folder, or parent the caller does not hold, <c>400</c> for a name
/// the hierarchy will not take, and <c>409</c> for an act the hierarchy's current state refuses.
/// </para>
/// </remarks>
internal static class ClientLocalMailFoldersEndpoint
{
    /// <summary>The route reading an account's folders and creating one, relative to the client prefix.</summary>
    internal const string LocalFoldersRoute = "/local-folders";

    /// <summary>The route renaming a folder.</summary>
    internal const string RenamesRoute = LocalFoldersRoute + "/renames";

    /// <summary>The route moving a folder with everything beneath it.</summary>
    internal const string MovesRoute = LocalFoldersRoute + "/moves";

    /// <summary>The route deleting a folder into the trash, or erasing one already there.</summary>
    internal const string DeletionsRoute = LocalFoldersRoute + "/deletions";

    /// <summary>The greatest request body a write route reads before refusing it.</summary>
    /// <remarks>
    /// A name of <see cref="LocalMailFolderName.MaximumLength" /> characters escaped at six bytes each, an account, and two
    /// identities, with room to spare; a body past it is answered <c>413</c> before the handler is reached.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 4096;

    /// <summary>Maps the routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientLocalMailFolders(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(LocalFoldersRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements IRequestSizeLimitMetadata,
        // which the routing pipeline applies to the request body.
        api.MapPost(LocalFoldersRoute, CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFoldersWrite);
        api.MapPost(RenamesRoute, RenameAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFoldersWrite);
        api.MapPost(MovesRoute, MoveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFoldersWrite);
        api.MapPost(DeletionsRoute, DeleteAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailFoldersWrite);
    }

    /// <summary>Reports whether MailFathom holds the account's mailbox, and the folders it keeps for it where it does.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="editor">Reads the account's folders.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the phase and the folders, <c>404</c> where the caller holds no such account, or <c>400</c> where no account is named.</returns>
    internal static async Task<Results<Ok<ClientLocalMailFoldersResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? account,
        [FromServices] LocalMailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        return await editor.ReadAsync(accountId, cancellationToken) is { } holding
            ? TypedResults.Ok(ClientLocalMailFoldersResponse.For(holding))
            : Refused(LocalMailFolderRefusal.AccountMissing);
    }

    /// <summary>Creates a folder.</summary>
    /// <param name="request">The account, the parent, and the name.</param>
    /// <param name="editor">Creates the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder created, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientLocalMailFolderEditResponse>, ProblemHttpResult>> CreateAsync(
        [FromBody] ClientLocalMailFolderCreateRequest request,
        [FromServices] LocalMailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(request.Account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        if (!TryReadFolder(request.ParentId, out var parentId))
        {
            return NoFolderNamed();
        }

        return Answer(await editor.CreateAsync(accountId, parentId, request.Name, cancellationToken));
    }

    /// <summary>Renames a folder.</summary>
    /// <param name="request">The account, the folder, and the new name.</param>
    /// <param name="editor">Renames the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder renamed, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientLocalMailFolderEditResponse>, ProblemHttpResult>> RenameAsync(
        [FromBody] ClientLocalMailFolderRenameRequest request,
        [FromServices] LocalMailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(request.Account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        if (!TryReadFolder(request.FolderId, out var folderId) || folderId is not { } folder)
        {
            return NoFolderNamed();
        }

        return Answer(await editor.RenameAsync(accountId, folder, request.Name, cancellationToken));
    }

    /// <summary>Moves a folder, with everything beneath it.</summary>
    /// <param name="request">The account, the folder, and its new parent.</param>
    /// <param name="editor">Moves the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder moved, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientLocalMailFolderEditResponse>, ProblemHttpResult>> MoveAsync(
        [FromBody] ClientLocalMailFolderMoveRequest request,
        [FromServices] LocalMailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(request.Account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        if (!TryReadFolder(request.FolderId, out var folderId)
            || folderId is not { } folder
            || !TryReadFolder(request.ParentId, out var parentId))
        {
            return NoFolderNamed();
        }

        return Answer(await editor.MoveAsync(accountId, folder, parentId, cancellationToken));
    }

    /// <summary>Deletes a folder into the trash, or erases it with its mail where it is already there.</summary>
    /// <param name="request">The account and the folder.</param>
    /// <param name="editor">Deletes the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder moved into the trash or erased, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientLocalMailFolderEditResponse>, ProblemHttpResult>> DeleteAsync(
        [FromBody] ClientLocalMailFolderDeleteRequest request,
        [FromServices] LocalMailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(request.Account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        if (!TryReadFolder(request.FolderId, out var folderId) || folderId is not { } folder)
        {
            return NoFolderNamed();
        }

        return Answer(await editor.DeleteAsync(accountId, folder, cancellationToken));
    }

    private static Results<Ok<ClientLocalMailFolderEditResponse>, ProblemHttpResult> Answer(LocalMailFolderEditOutcome outcome) =>
        outcome is { Folder: { } folder, Kind: { } kind }
            ? TypedResults.Ok(new ClientLocalMailFolderEditResponse(kind.ToString(), ClientLocalMailFolderResponse.For(folder), outcome.MailErasureDeferred))
            : Refused(outcome.Refusal ?? throw new InvalidOperationException("An edit outcome carried neither a folder nor a refusal."));

    private static MailAccountId? AccountNamed(string? account) =>
        string.IsNullOrWhiteSpace(account) ? null : MailAccountId.Create(account);

    /// <summary>Reads an optional folder identity, refusing the empty one a client never received.</summary>
    private static bool TryReadFolder(Guid? value, out LocalMailFolderId? folder)
    {
        folder = value is { } identity && identity != Guid.Empty ? LocalMailFolderId.Create(identity) : null;

        return value != Guid.Empty;
    }

    private static ProblemHttpResult NoAccountNamed() =>
        TypedResults.Problem("The request names no account.", statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NoFolderNamed() =>
        TypedResults.Problem("The request names no folder, or names one by a value this deployment does not issue.", statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Answers a refusal with its status, a detail a person can read, and its own name for a client to branch on.</summary>
    /// <param name="refusal">What the use case refused.</param>
    /// <returns>The problem response.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a refusal this surface has no answer for.</exception>
    internal static ProblemHttpResult Refused(LocalMailFolderRefusal refusal)
    {
        var (status, detail) = refusal switch
        {
            LocalMailFolderRefusal.AccountMissing => (StatusCodes.Status404NotFound, "You hold no such account."),
            LocalMailFolderRefusal.FolderMissing => (StatusCodes.Status404NotFound, "The account holds no such folder."),
            LocalMailFolderRefusal.ParentMissing => (StatusCodes.Status404NotFound, "The account holds no such parent folder."),
            LocalMailFolderRefusal.NameInvalid => (StatusCodes.Status400BadRequest, $"A folder name is 1 to {LocalMailFolderName.MaximumLength} characters, with no control or format character and no '{LocalMailFolderName.HierarchyDelimiter}'."),
            LocalMailFolderRefusal.InboxNameAtTopLevel => (StatusCodes.Status400BadRequest, "The top of the hierarchy already has the inbox, and no other folder there may be named INBOX."),
            LocalMailFolderRefusal.AccountNotHeld => (StatusCodes.Status409Conflict, "Folders can be edited only while MailFathom holds the account's mailbox, and this account is mirrored or restoring."),
            LocalMailFolderRefusal.ProtectedRole => (StatusCodes.Status409Conflict, "The inbox, drafts, sent, junk, and trash folders cannot be renamed, moved, or deleted."),
            LocalMailFolderRefusal.NameTaken => (StatusCodes.Status409Conflict, "A folder beside it already has that name."),
            LocalMailFolderRefusal.NestedInItself => (StatusCodes.Status409Conflict, "A folder cannot be moved beneath itself."),
            LocalMailFolderRefusal.TooDeep => (StatusCodes.Status409Conflict, $"A hierarchy is at most {LocalMailFolderTree.MaximumDepth} levels deep."),
            LocalMailFolderRefusal.TooManyFolders => (StatusCodes.Status409Conflict, $"An account holds at most {LocalMailFolderTree.MaximumFolders} folders."),
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "The refusal has no answer on this surface."),
        };

        return TypedResults.Problem(
            detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["refusal"] = refusal.ToString() });
    }
}

/// <summary>A folder to create.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="ParentId">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
/// <param name="Name">The folder's name.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientLocalMailFolderCreateRequest(string? Account = null, Guid? ParentId = null, string? Name = null);

/// <summary>A folder to rename.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="FolderId">The folder.</param>
/// <param name="Name">The new name.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientLocalMailFolderRenameRequest(string? Account = null, Guid? FolderId = null, string? Name = null);

/// <summary>A folder to move.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="FolderId">The folder.</param>
/// <param name="ParentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientLocalMailFolderMoveRequest(string? Account = null, Guid? FolderId = null, Guid? ParentId = null);

/// <summary>A folder to delete.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="FolderId">The folder.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientLocalMailFolderDeleteRequest(string? Account = null, Guid? FolderId = null);

/// <summary>What the client endpoint reports about an account's local folders.</summary>
/// <param name="Phase">Whether MailFathom mirrors, holds, or is restoring the account's mailbox, as the phase's own name.</param>
/// <param name="Folders">The live folders, empty unless the mailbox is held, ordered by name.</param>
internal sealed record ClientLocalMailFoldersResponse(string Phase, IReadOnlyList<ClientLocalMailFolderResponse> Folders)
{
    /// <summary>Describes a holding on the wire.</summary>
    /// <param name="holding">What the use case answered.</param>
    /// <returns>The response body.</returns>
    internal static ClientLocalMailFoldersResponse For(LocalMailFolderHolding holding) => new(
        holding.Phase.ToString(),
        [.. holding.Folders.OrderBy(static folder => folder.Name.ComparisonKey, StringComparer.Ordinal).Select(ClientLocalMailFolderResponse.For)]);
}

/// <summary>One local folder.</summary>
/// <param name="Id">The folder's identity, which every write names it by.</param>
/// <param name="ParentId">The folder it sits beneath, or <see langword="null" /> at the top of the hierarchy.</param>
/// <param name="Name">The folder's name.</param>
/// <param name="Role">The protected role it plays, as the role's own name, or <see langword="null" /> where it plays none.</param>
internal sealed record ClientLocalMailFolderResponse(Guid Id, Guid? ParentId, string Name, string? Role)
{
    /// <summary>Describes one folder on the wire.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The response body.</returns>
    internal static ClientLocalMailFolderResponse For(LocalMailFolder folder) => new(
        folder.Id.Value,
        folder.ParentId?.Value,
        folder.Name.Value,
        folder.Role?.ToString());
}

/// <summary>What an accepted write changed.</summary>
/// <param name="Change">Which change it was — <c>Created</c>, <c>Renamed</c>, <c>Moved</c>, <c>MovedToTrash</c>, or <c>Erased</c>.</param>
/// <param name="Folder">The folder as the write left it; an erased folder is reported as it was at its erasure.</param>
/// <param name="MailErasureDeferred">Whether an erasure found the job queue full, so its mail waits for the account's next erasure.</param>
internal sealed record ClientLocalMailFolderEditResponse(string Change, ClientLocalMailFolderResponse Folder, bool MailErasureDeferred);
