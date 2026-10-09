// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

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
            "The user to act for, by the identifier the deployment gave their record. Optional where the deployment "
            + "holds one user, which is settled by reading the roster — so an invocation naming none needs "
            + "mailfathom.admin.read on the credential beside whatever the command itself is published under.",
    };

    /// <summary>Settles on the user a command acts for, asking the deployment where the invocation named none.</summary>
    /// <param name="deployment">The client already reaching the deployment.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="requestedUser">The user the invocation named, or <see langword="null" /> where it named none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The user to act for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deployment" /> or <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment serves no user on a mail endpoint, or serves several and the invocation named none.</exception>
    /// <remarks>
    /// <para>
    /// A named user is used as written and never checked against the roster first: the deployment refuses a user it
    /// holds no record for and says so, and a lookup here would only decide the same thing one request earlier while
    /// telling the operator which identifiers exist. The empty identifier is a stated user like any other — an unset
    /// script variable expands to one, and reading it as "no user was named" would act on the single user a
    /// deployment happens to hold instead of refusing an invocation that named nobody.
    /// </para>
    /// <para>
    /// Only a user served on a mail endpoint settles it. The default administrator every deployment records is on the
    /// roster with both endpoints switched off, so counting it would act on the administrator of a fresh deployment and
    /// refuse every deployment that has since recorded the one user it serves; a command meant for a user kept off both
    /// endpoints names them.
    /// </para>
    /// <para>
    /// Settling the user instead reads the roster, which is published under <c>mailfathom.admin.read</c> while the
    /// commands reaching this are published under grants of their own, and no permission here implies another. A
    /// credential provisioned for one surface alone is therefore refused at this read rather than at the act, on
    /// exactly the single-user deployment where the option is meant to resolve itself; naming the user avoids it.
    /// </para>
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
        UserRosterEntry[] served = [.. (roster.Users ?? []).Where(user => user.McpEndpoint || user.ClientEndpoint)];

        return served switch
        {
            [var only] => only.Id,
            [] => throw new CliFailure(
                "The deployment serves no user on a mail endpoint, so there is nobody to act for. Record one with "
                + "'user add', or pass --user to act for a user kept off both endpoints, such as the default administrator."),
            var several => throw new CliFailure(
                $"The deployment serves {several.Length} users, so which one this acts for has to be said. Pass --user "
                + $"with one of: {string.Join(", ", several.Select(user => user.Describe()))}"),
        };
    }
}
