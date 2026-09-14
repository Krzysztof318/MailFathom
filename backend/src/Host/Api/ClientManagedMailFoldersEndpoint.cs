// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Folders;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves an account's folders as things a person may act on, and takes the four acts they may ask for.</summary>
/// <remarks>
/// <para>
/// It is one surface for every account. Nothing a request carries and nothing an answer returns says where the
/// account's mail is kept: an act is asked for the same way whether the mailbox is the mail server's or MailFathom's
/// own, and which of the two carries it is the service's to decide. A client branching on a storage mode would be
/// wrong about any account whose mode changed after it read one.
/// </para>
/// <para>
/// What replaces that branch is the report this route's read carries: the account says which acts it allows and which
/// roles a folder may still be created for, and each folder says which acts it allows. A client draws its menus from
/// that, rather than from a rule it holds a copy of.
/// </para>
/// <para>
/// A folder is named by an identity whose shape is not part of the contract, so a rename or a move never invalidates a
/// reference a client already holds. Each write is a route of its own, posted, so the verb and the path say which act a
/// request is and a body can never be read as a different one.
/// </para>
/// <para>
/// A refusal carries the refusal's own name as <c>refusal</c> beside its detail, so a client can draw the right message
/// without parsing prose: <c>404</c> for an account, folder, or parent the caller does not hold, <c>400</c> for a name
/// the hierarchy will not take, <c>409</c> for an act the account's current state refuses, and <c>502</c> or
/// <c>503</c> for a mail server that refused the act or did not answer.
/// </para>
/// </remarks>
internal static class ClientManagedMailFoldersEndpoint
{
    /// <summary>The route reading an account's folders with their acts, and creating one, relative to the client prefix.</summary>
    internal const string ManagedFoldersRoute = "/managed-folders";

    /// <summary>The route renaming a folder.</summary>
    internal const string RenamesRoute = ManagedFoldersRoute + "/renames";

    /// <summary>The route moving a folder with everything beneath it.</summary>
    internal const string MovesRoute = ManagedFoldersRoute + "/moves";

    /// <summary>The route deleting a folder.</summary>
    internal const string DeletionsRoute = ManagedFoldersRoute + "/deletions";

    /// <summary>The greatest request body a write route reads before refusing it.</summary>
    /// <remarks>
    /// A name of <see cref="LocalMailFolderName.MaximumLength" /> characters escaped at six bytes each, an account, and
    /// the two folder identities beside it, with room to spare. Those identities are opaque text rather than a fixed
    /// width — on a mirrored account each is the folder's alias, which carries no maximum length of its own — so this is
    /// the bound on them: a body past it is answered <c>413</c> before the handler is reached.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 4096;

    /// <summary>Maps the routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientManagedMailFolders(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(ManagedFoldersRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter: it implements IRequestSizeLimitMetadata,
        // which the routing pipeline applies to the request body.
        api.MapPost(ManagedFoldersRoute, CreateAsync)
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

    /// <summary>Reports the account's folders and which acts each of them and the account itself allow.</summary>
    /// <param name="account">The caller's account.</param>
    /// <param name="editor">Reads the account's folders.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the folders and their acts, <c>404</c> where the caller holds no such account, or <c>400</c> where no account is named.</returns>
    internal static async Task<Results<Ok<ClientManagedMailFoldersResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? account,
        [FromServices] MailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        return await editor.ReadAsync(accountId, cancellationToken) is { } management
            ? TypedResults.Ok(ClientManagedMailFoldersResponse.For(management))
            : Refused(MailFolderActRefusal.AccountMissing);
    }

    /// <summary>Creates a folder, either by naming it or by naming the role it is to play.</summary>
    /// <param name="request">The account, the parent, and either the name or the role.</param>
    /// <param name="editor">Creates the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder created, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientManagedMailFolderActResponse>, ProblemHttpResult>> CreateAsync(
        [FromBody] ClientManagedMailFolderCreateRequest request,
        [FromServices] MailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        if (AccountNamed(request.Account) is not { } accountId)
        {
            return NoAccountNamed();
        }

        if (!TryReadRole(request.Role, out var role))
        {
            return NoRoleNamed();
        }

        return Answer(await editor.CreateAsync(accountId, request.ParentId, request.Name, role, cancellationToken));
    }

    /// <summary>Renames a folder, leaving it where it is.</summary>
    /// <param name="request">The account, the folder, and the new name.</param>
    /// <param name="editor">Renames the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder renamed, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientManagedMailFolderActResponse>, ProblemHttpResult>> RenameAsync(
        [FromBody] ClientManagedMailFolderRenameRequest request,
        [FromServices] MailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        return AccountNamed(request.Account) is { } accountId
            ? Answer(await editor.RenameAsync(accountId, request.FolderId, request.Name, cancellationToken))
            : NoAccountNamed();
    }

    /// <summary>Moves a folder, with everything beneath it.</summary>
    /// <param name="request">The account, the folder, and its new parent.</param>
    /// <param name="editor">Moves the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder moved, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientManagedMailFolderActResponse>, ProblemHttpResult>> MoveAsync(
        [FromBody] ClientManagedMailFolderMoveRequest request,
        [FromServices] MailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        return AccountNamed(request.Account) is { } accountId
            ? Answer(await editor.MoveAsync(accountId, request.FolderId, request.ParentId, cancellationToken))
            : NoAccountNamed();
    }

    /// <summary>Deletes a folder, which means whatever the account's own rules make it mean.</summary>
    /// <param name="request">The account and the folder.</param>
    /// <param name="editor">Deletes the folder.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with the folder as the act left it, or the refusal.</returns>
    internal static async Task<Results<Ok<ClientManagedMailFolderActResponse>, ProblemHttpResult>> DeleteAsync(
        [FromBody] ClientManagedMailFolderDeleteRequest request,
        [FromServices] MailFolderEditor editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(editor);

        return AccountNamed(request.Account) is { } accountId
            ? Answer(await editor.DeleteAsync(accountId, request.FolderId, cancellationToken))
            : NoAccountNamed();
    }

    private static Results<Ok<ClientManagedMailFolderActResponse>, ProblemHttpResult> Answer(MailFolderActOutcome outcome) =>
        outcome is { Folder: { } folder, Change: { } change }
            ? TypedResults.Ok(new ClientManagedMailFolderActResponse(
                change.ToString(),
                ClientManagedMailFolderResponse.For(folder),
                outcome.MailErasureDeferred))
            : Refused(outcome.Refusal ?? throw new InvalidOperationException("An act outcome carried neither a folder nor a refusal."));

    private static MailAccountId? AccountNamed(string? account) =>
        string.IsNullOrWhiteSpace(account) ? null : MailAccountId.Create(account);

    /// <summary>Reads the role a creation named, refusing anything but the roles a folder may be created for.</summary>
    private static bool TryReadRole(string? value, out MailFolderSpecialUse? role)
    {
        role = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<MailFolderSpecialUse>(value.Trim(), ignoreCase: true, out var named)
            || !MailFolderRoleNaming.Creatable.Contains(named))
        {
            return false;
        }

        role = named;

        return true;
    }

    private static ProblemHttpResult NoAccountNamed() =>
        TypedResults.Problem("The request names no account.", statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NoRoleNamed() =>
        TypedResults.Problem(
            $"A folder is created for one of these roles or for none: {string.Join(", ", MailFolderRoleNaming.Creatable)}.",
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Answers a refusal with its status, a detail a person can read, and its own name for a client to branch on.</summary>
    /// <param name="refusal">What the use case refused.</param>
    /// <returns>The problem response.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a refusal this surface has no answer for.</exception>
    internal static ProblemHttpResult Refused(MailFolderActRefusal refusal)
    {
        var (status, detail) = refusal switch
        {
            MailFolderActRefusal.AccountMissing => (StatusCodes.Status404NotFound, "You hold no such account."),
            MailFolderActRefusal.FolderMissing => (StatusCodes.Status404NotFound, "The account holds no such folder."),
            MailFolderActRefusal.ParentMissing => (StatusCodes.Status404NotFound, "The account holds no such parent folder."),
            MailFolderActRefusal.NameInvalid => (StatusCodes.Status400BadRequest, $"A folder name is 1 to {LocalMailFolderName.MaximumLength} characters, with no control or format character and no '{LocalMailFolderName.HierarchyDelimiter}'."),
            MailFolderActRefusal.InboxNameAtTopLevel => (StatusCodes.Status400BadRequest, "The top of the hierarchy already has the inbox, and no other folder there may be named INBOX."),
            MailFolderActRefusal.AccountNotHeld => (StatusCodes.Status409Conflict, "The account's mailbox is being restored to its mail server, and its folders can be acted on again once that ends."),
            MailFolderActRefusal.ProtectedRole => (StatusCodes.Status409Conflict, "The inbox, drafts, sent, junk, and trash folders cannot be renamed, moved, or deleted."),
            MailFolderActRefusal.NameTaken => (StatusCodes.Status409Conflict, "A folder beside it already has that name."),
            MailFolderActRefusal.NestedInItself => (StatusCodes.Status409Conflict, "A folder cannot be moved beneath itself."),
            MailFolderActRefusal.TooDeep => (StatusCodes.Status409Conflict, $"A hierarchy is at most {LocalMailFolderTree.MaximumDepth} levels deep."),
            MailFolderActRefusal.TooManyFolders => (StatusCodes.Status409Conflict, $"An account holds at most {LocalMailFolderTree.MaximumFolders} folders."),
            MailFolderActRefusal.RoleAlreadyPlayed => (StatusCodes.Status409Conflict, "The account already has a folder for that role, and one account has at most one folder per role."),
            MailFolderActRefusal.NotDeclaredByTheAccount => (StatusCodes.Status409Conflict, "This folder is one the deployment's own configuration states, so it is changed by whoever administers the deployment rather than here."),
            MailFolderActRefusal.ServerRefused => (StatusCodes.Status502BadGateway, "The account's mail server refused the act, and nothing was changed."),
            MailFolderActRefusal.ServerUnavailable => (StatusCodes.Status503ServiceUnavailable, "The account's mail server did not answer, and nothing was changed."),
            MailFolderActRefusal.NotRecorded => (StatusCodes.Status500InternalServerError, "The act reached the account's mail server and MailFathom could not record it. Read the folders again before asking for anything else."),
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
/// <param name="Name">The folder's name, which a creation naming a role does not carry.</param>
/// <param name="Role">The role the folder is to play, as the role's own name, or <see langword="null" /> for an ordinary folder.</param>
/// <remarks>A creation naming a role names neither a name nor a parent: the service gives such a folder the role's standard name at the top of the hierarchy, so the two are ignored rather than refused.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientManagedMailFolderCreateRequest(
    string? Account = null,
    string? ParentId = null,
    string? Name = null,
    string? Role = null);

/// <summary>A folder to rename.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="FolderId">The folder.</param>
/// <param name="Name">The new name.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientManagedMailFolderRenameRequest(string? Account = null, string? FolderId = null, string? Name = null);

/// <summary>A folder to move.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="FolderId">The folder.</param>
/// <param name="ParentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientManagedMailFolderMoveRequest(string? Account = null, string? FolderId = null, string? ParentId = null);

/// <summary>A folder to delete.</summary>
/// <param name="Account">The caller's account.</param>
/// <param name="FolderId">The folder.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientManagedMailFolderDeleteRequest(string? Account = null, string? FolderId = null);

/// <summary>What the client endpoint reports about an account's folders and the acts they allow.</summary>
/// <param name="AllowedActs">The acts the account itself allows, which is <c>Create</c> or nothing.</param>
/// <param name="CreatableRoles">The roles the account still has no folder for, each of which a creation may name, empty where it has one for every role.</param>
/// <param name="Folders">The account's folders, ordered by name.</param>
internal sealed record ClientManagedMailFoldersResponse(
    IReadOnlyList<string> AllowedActs,
    IReadOnlyList<string> CreatableRoles,
    IReadOnlyList<ClientManagedMailFolderResponse> Folders)
{
    /// <summary>Describes what the account allows on the wire.</summary>
    /// <param name="management">What the use case answered.</param>
    /// <returns>The response body.</returns>
    internal static ClientManagedMailFoldersResponse For(MailFolderManagement management) => new(
        [.. management.AllowedActs.Select(static act => act.ToString())],
        [.. management.CreatableRoles.Select(static role => role.ToString())],
        [.. management.Folders.Select(ClientManagedMailFolderResponse.For)]);
}

/// <summary>One folder a person may act on.</summary>
/// <param name="Id">The folder's identity, which every write names it by.</param>
/// <param name="ParentId">The folder it sits beneath, or <see langword="null" /> at the top of the hierarchy.</param>
/// <param name="Name">The folder's name.</param>
/// <param name="Role">The role it plays, as the role's own name, or <see langword="null" /> where it plays none.</param>
/// <param name="AllowedActs">The acts this folder allows, each as the act's own name, empty where it allows none.</param>
internal sealed record ClientManagedMailFolderResponse(
    string Id,
    string? ParentId,
    string Name,
    string? Role,
    IReadOnlyList<string> AllowedActs)
{
    /// <summary>Describes one folder on the wire.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The response body.</returns>
    internal static ClientManagedMailFolderResponse For(ManagedMailFolder folder) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        folder.Role?.ToString(),
        [.. folder.AllowedActs.Select(static act => act.ToString())]);
}

/// <summary>What an accepted act changed.</summary>
/// <param name="Change">Which change it was — <c>Created</c>, <c>Renamed</c>, <c>Moved</c>, <c>MovedToTrash</c>, <c>Erased</c>, <c>Deleted</c>, or <c>MarkedDeleted</c>.</param>
/// <param name="Folder">The folder as the act left it; a folder that is gone is reported as it was at the act.</param>
/// <param name="MailErasureDeferred">Whether removing the folder's stored mail found the job queue full, so it waits for the account's next erasure.</param>
internal sealed record ClientManagedMailFolderActResponse(string Change, ClientManagedMailFolderResponse Folder, bool MailErasureDeferred);
