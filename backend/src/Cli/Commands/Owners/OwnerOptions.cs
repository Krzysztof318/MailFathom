// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>How an owner is named, and how a command settles on one when the invocation named none.</summary>
/// <remarks>
/// Two families of commands act for an owner — what the deployment records about them, and the credentials they sign in
/// with — so which owner is meant is settled once here rather than twice. The option is optional because the deployment
/// this serves usually holds one person: a command given no owner asks which owners exist and acts on the single one,
/// and refuses rather than guessing where there are several, so the ordinary invocation names nothing and the ambiguous
/// one is told exactly what to add.
/// </remarks>
internal static class OwnerOptions
{
    /// <summary>Builds the option naming which owner a command acts for.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid?> Owner() => new("--owner")
    {
        Description =
            "The owner to act for, by the identifier the deployment gave their record. Optional where the deployment holds one owner.",
    };

    /// <summary>Settles on the owner a command acts for, asking the deployment where the invocation named none.</summary>
    /// <param name="deployment">The client already reaching the deployment.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="requestedOwner">The owner the invocation named, or <see langword="null" /> where it named none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The owner to act for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deployment" /> or <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment holds no owner at all, or holds several and the invocation named none.</exception>
    /// <remarks>
    /// A named owner is used as written and never checked against the roster first: the deployment refuses an owner it
    /// holds no record for and says so, and a lookup here would only decide the same thing one request earlier while
    /// telling the operator which identifiers exist. The empty identifier is a stated owner like any other — an unset
    /// script variable expands to one, and reading it as "no owner was named" would act on the single owner a
    /// deployment happens to hold instead of refusing an invocation that named nobody.
    /// </remarks>
    internal static async Task<Guid> ResolveOwnerAsync(
        AdminApiClient deployment,
        string token,
        Guid? requestedOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(token);

        if (requestedOwner is { } named)
        {
            return named;
        }

        var roster = await deployment.ReadOwnersAsync(token, cancellationToken);

        return roster.Owners switch
        {
            [var only] => only.Id,
            null or [] => throw new CliFailure(
                "The deployment holds no owner records, so there is nobody to act for. An owner record is written when "
                + "the deployment first composes its settings, and 'owner add' records another; check that it started "
                + "successfully."),
            var several => throw new CliFailure(
                $"The deployment holds {several.Count} owners, so which one this acts for has to be said. Pass --owner "
                + $"with one of: {string.Join(", ", several.Select(owner => owner.Describe()))}"),
        };
    }
}
