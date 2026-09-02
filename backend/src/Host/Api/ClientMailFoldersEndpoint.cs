// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in owner's mailboxes and their folders as the one tree a mail screen is drawn from.</summary>
/// <remarks>
/// <para>
/// It is the route a client reads before it can show anybody anything: a folder tree is the scope every other read
/// takes, so nothing on this surface has a scope until this has answered. It carries the accounts as well as the
/// folders, because a tree is one thing on screen — a client that read the folders here and the mailbox names from
/// <see cref="ClientMailAccountsEndpoint" /> would be composing one picture out of two answers, the second already
/// stale relative to the first.
/// </para>
/// <para>
/// Each folder carries the role it plays, which is the answer a screen cannot work out for itself: special-use folders
/// are advertised by attribute rather than by name, and the names differ per provider and per language. It carries its
/// place in the server's hierarchy for the same reason, since MailFathom's own alias for a folder is one upper-cased
/// configured word and a tree is not drawn from those.
/// </para>
/// <para>
/// It costs more than the accounts route and is therefore a route of its own rather than a wider version of it.
/// Counting a folder's mail is work proportional to the mail, so a client polling for whether a mailbox is reachable
/// asks there and a client drawing a tree asks here.
/// </para>
/// <para>
/// Nothing of the mailbox reaches it beyond the folders themselves: no message, no subject, no correspondent, and no
/// mail server, port, user name, or credential. The remote folder names it does carry are this owner's own, on a
/// surface only this owner reaches — which is why the administrative status surface, read across every owner by
/// somebody administering the deployment, publishes aliases and no paths at all.
/// </para>
/// </remarks>
internal static class ClientMailFoldersEndpoint
{
    /// <summary>The route reporting the owner's folders, relative to the client prefix.</summary>
    internal const string MailFoldersRoute = "/folders";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailFolders(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailFoldersRoute, ReadFoldersAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Reports the acting owner's mailboxes, their folders, and how current each of them is.</summary>
    /// <param name="reader">Reads the owner's accounts and the folders a screen may draw beneath them.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the owner's tree, empty where they own no account, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>It speaks to no mail server, so a client request cannot wait on IMAP and cannot set the remote <c>\Seen</c> flag.</remarks>
    internal static async Task<Ok<ClientMailFoldersResponse>> ReadFoldersAsync(
        [FromServices] MailFolderDirectoryReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var directory = await reader.ReadAsync(cancellationToken);

        return TypedResults.Ok(ClientMailFoldersResponse.For(directory));
    }
}

/// <summary>What the client endpoint reports about the owner's mailboxes and their folders.</summary>
/// <param name="SynchronizationEnabled">Whether this deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">One entry per account the acting owner owns, ordered by identifier, empty where they own none.</param>
/// <remarks>
/// The switch is reported beside the accounts because no per-folder value carries it: a folder that last moved a week
/// ago means one thing where the deployment is still trying and another where it has stopped, and a client that could
/// not tell the two apart would show every folder as failing or none of them.
/// </remarks>
internal sealed record ClientMailFoldersResponse(
    bool SynchronizationEnabled,
    IReadOnlyList<ClientMailFolderAccountResponse> Accounts)
{
    /// <summary>Describes the owner's tree on the wire.</summary>
    /// <param name="directory">What the use case answered.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory" /> is <see langword="null" />.</exception>
    internal static ClientMailFoldersResponse For(MailFolderDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return new ClientMailFoldersResponse(
            directory.SynchronizationEnabled,
            [.. directory.Accounts.Select(ClientMailFolderAccountResponse.For)]);
    }
}

/// <summary>One of the owner's accounts and the folders beneath it.</summary>
/// <param name="Account">The account, exactly as the accounts route publishes it.</param>
/// <param name="Folders">The account's folders, ordered by alias, empty where synchronization has reached none.</param>
/// <remarks>
/// The account is the accounts route's own type rather than a copy of its fields, so the two routes cannot come to
/// disagree about what an account is. That is why it is nested rather than flattened: a copy flattened for the
/// convenience of one screen would be four field names to keep in step with another route forever.
/// </remarks>
internal sealed record ClientMailFolderAccountResponse(
    ClientMailAccountResponse Account,
    IReadOnlyList<ClientMailFolderResponse> Folders)
{
    /// <summary>Describes one account and its folders on the wire.</summary>
    /// <param name="account">The account's folders.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailFolderAccountResponse For(MailAccountFolders account) => new(
        ClientMailAccountResponse.For(account.Account),
        [.. account.Folders.Select(ClientMailFolderResponse.For)]);
}

/// <summary>One folder of one account, as a screen drawing a tree needs it.</summary>
/// <param name="Alias">MailFathom's own name for the folder, which is what every other route on this surface names it by.</param>
/// <param name="Role">The role the folder plays for its account, as the role's own name, or <see langword="null" /> where configuration labels it with none.</param>
/// <param name="Path">The folder's place on its mail server, outermost level first, and empty where nothing has bound the alias to a remote folder yet.</param>
/// <param name="StoredEmailCount">How many of the folder's emails this deployment holds and would serve.</param>
/// <param name="UnreadEmailCount">How many of those the mail server last reported without <c>\Seen</c>.</param>
/// <param name="SynchronizationState">Whether the deployment's last attempt at the folder succeeded, failed, found no server, or has never happened, as the state's own name.</param>
/// <param name="LastSynchronizedAt">When the folder last durably took anything in, or <see langword="null" /> where it never has.</param>
/// <param name="Behind">Whether the folder's last attempt ended with mail it had not yet taken in.</param>
/// <remarks>
/// <para>
/// The path is levels rather than one string, so a client builds a tree without knowing that a mail server has a
/// hierarchy delimiter or which character this one chose. The last level is what a person recognizes as the folder's
/// name; the alias above it is MailFathom's own and is what a later request names the folder with.
/// </para>
/// <para>
/// The counts are of the local copy and are meaningless without the three fields under them, which is why all six
/// travel together. A folder still being backfilled holds fewer than the server does, and a folder whose last attempt
/// failed holds what it held before that attempt.
/// </para>
/// </remarks>
internal sealed record ClientMailFolderResponse(
    string Alias,
    string? Role,
    IReadOnlyList<string> Path,
    int StoredEmailCount,
    int UnreadEmailCount,
    string SynchronizationState,
    DateTimeOffset? LastSynchronizedAt,
    bool Behind)
{
    /// <summary>Describes one folder on the wire.</summary>
    /// <param name="folder">The folder as the use case described it.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailFolderResponse For(DescribedMailFolder folder) => new(
        folder.Alias.Value,
        folder.Role?.ToString(),
        folder.HierarchyLevels,
        folder.StoredEmailCount,
        folder.UnreadEmailCount,
        folder.Freshness.State.ToString(),
        folder.Freshness.SynchronizedAt,
        folder.Freshness.IsBehind);
}
