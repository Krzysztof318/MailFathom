// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Reads the users this deployment holds records for.</summary>
/// <remarks>
/// It answers about the user records themselves rather than about anything they own, which is why it is the whole of
/// this port: what a user's mail accounts are is the account catalog's question, and what a user has configured is
/// their own document's. What it is asked is who is on the list, what each of them is called, and whether anybody is
/// on it at all — an administrator's listing, one person's own envelope, and the provisioning that refuses a second
/// user where a surface could not tell two apart.
/// </remarks>
public interface IUserDirectory
{
    /// <summary>Reads the users this deployment holds, at most as many as asked for.</summary>
    /// <param name="limit">The greatest number of users to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The users, in a stable order, and no more than <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// The bound is the caller's, and it exists so that a caller asking whether anybody is recorded at all reads as few
    /// rows as that question needs rather than every user. The order is stable so that a caller asking for one user
    /// twice is answered about the same user both times.
    /// </remarks>
    Task<IReadOnlyList<UserRecord>> ReadUsersAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Reads one page of the users some scopes cover, in identifier order.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="within">The scopes the listing answers within: every user where the deployment scope is among them, and otherwise the members of each organization and each user named.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and where the following one continues; empty where no scope is given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> or <paramref name="within" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is the administrative listing's read, which walks the users a page at a time rather than reading as many as a
    /// caller bounds it to. The scopes filter the query rather than the page, so a page holds as many covered users as it
    /// is asked for and a caller scoped to one organization never pages through everybody else's.
    /// </remarks>
    Task<AdministrativeListingPage<UserRecord>> ReadUserPageAsync(
        AdministrativeListingQuery query,
        IReadOnlySet<AssignmentScope> within,
        CancellationToken cancellationToken);

    /// <summary>Reads the envelope of one user this deployment holds.</summary>
    /// <param name="user">The user whose envelope is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The user's envelope, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// It stands beside the roster rather than being composed from it, because the two answer for different callers.
    /// A user-facing surface asks what this deployment records about the person who authenticated, and reading the
    /// roster to filter it down to one would compose a deployment-wide catalog of people to answer a question about
    /// one of them. The label is what such a surface is after: it is the one thing the envelope holds that a person is
    /// shown, and it is not in the document beside it.
    /// </remarks>
    Task<UserRecord?> ReadUserAsync(UserId user, CancellationToken cancellationToken);
}
