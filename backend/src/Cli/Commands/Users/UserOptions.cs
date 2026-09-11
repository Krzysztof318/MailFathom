// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Users;

/// <summary>How a user is named, and how a command settles on one when the invocation named none.</summary>
/// <remarks>
/// Two families of commands act for a user — what the deployment records about them, and the credentials they sign in
/// with — so which user is meant is settled once here rather than twice. The option is optional because the deployment
/// this serves usually holds one person: a command given no user asks which users exist and acts on the single one,
/// and refuses rather than guessing where there are several, so the ordinary invocation names nothing and the ambiguous
/// one is told exactly what to add.
/// </remarks>
internal static class UserOptions
{
    /// <summary>Builds the option naming which user a command acts for.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid?> User() => new("--user")
    {
        Description =
            "The user to act for, by the identifier the deployment gave their record. Optional where the deployment holds one user.",
    };

    /// <summary>Settles on the user a command acts for, asking the deployment where the invocation named none.</summary>
    /// <param name="deployment">The client already reaching the deployment.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="requestedUser">The user the invocation named, or <see langword="null" /> where it named none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The user to act for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deployment" /> or <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment holds no user at all, or holds several and the invocation named none.</exception>
    /// <remarks>
    /// A named user is used as written and never checked against the roster first: the deployment refuses a user it
    /// holds no record for and says so, and a lookup here would only decide the same thing one request earlier while
    /// telling the operator which identifiers exist. The empty identifier is a stated user like any other — an unset
    /// script variable expands to one, and reading it as "no user was named" would act on the single user a
    /// deployment happens to hold instead of refusing an invocation that named nobody.
    /// </remarks>
    internal static async Task<Guid> ResolveUserAsync(
        AdminApiClient deployment,
        string token,
        Guid? requestedUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(token);

        if (requestedUser is { } named)
        {
            return named;
        }

        var roster = await deployment.ReadUsersAsync(token, cancellationToken);

        return roster.Users switch
        {
            [var only] => only.Id,
            null or [] => throw new CliFailure(
                "The deployment holds no user records, so there is nobody to act for. A fresh database is seeded "
                + "with one, so every user it held has been removed; record one with 'user add'."),
            var several => throw new CliFailure(
                $"The deployment holds {several.Count} users, so which one this acts for has to be said. Pass --user "
                + $"with one of: {string.Join(", ", several.Select(user => user.Describe()))}"),
        };
    }
}
