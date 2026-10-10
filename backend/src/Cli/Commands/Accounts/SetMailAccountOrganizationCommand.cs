// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Accounts;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Moves one mail account into an organization, or out of every organization.</summary>
/// <remarks>
/// <para>
/// An account in an organization is assigned only to that organization's members, and an account in none to one user
/// in none, so a move is refused while the account is assigned to anybody the target would not admit, and a move out
/// of every organization while it is assigned to more than one user. Nothing is unassigned on the operator's behalf.
/// </para>
/// <para>
/// Leaving every organization takes a word of its own, <c>--none</c>, for the reason <c>user set-organization</c> gives:
/// an unset script variable would otherwise read as a decision about who the account may be assigned to.
/// </para>
/// </remarks>
internal static class SetMailAccountOrganizationCommand
{
    /// <summary>Builds the <c>account set-organization</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = MailAccountOptions.Account();

        Option<Guid?> organizationOption = new("--organization")
        {
            Description = "The organization the account belongs to from now on, by the identifier 'organization list' reports. Refused beside '--none'.",
        };

        Option<bool> noneOption = new("--none")
        {
            Description = "Take the account out of every organization, so it is one user's alone. Refused while it is assigned to more than one user, and beside '--organization'.",
        };

        Command command = new("set-organization", "Move one mail account into an organization, or out of every organization.")
        {
            accountOption,
            organizationOption,
            noneOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            ResolveOrganization(result.GetValue(organizationOption), result.GetValue(noneOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Settles on the organization a move names, refusing an invocation that named both or neither.</summary>
    private static MailAccountOrganizationRequest ResolveOrganization(Guid? organization, bool none) => (organization, none) switch
    {
        ({ }, true) => throw new CliFailure(
            "The invocation both names an organization and says the account belongs to none. Drop '--none' to move it "
            + "into it, or drop '--organization' to take it out of every organization."),
        (null, false) => throw new CliFailure(
            "The invocation names no organization. Pass '--organization' to move the account into one, or '--none' to "
            + "take it out of every organization."),
        _ => new MailAccountOrganizationRequest(organization, none),
    };

    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        MailAccountOrganizationRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.SetMailAccountOrganizationAsync(profile.Token, accountId, request, cancellationToken);

        context.Console.WriteLine(
            request.OrganizationId is { } organization
                ? $"Mail account {accountId:D} now belongs to organization {organization:D}."
                : $"Mail account {accountId:D} now belongs to no organization.");

        return CliExitCode.Success;
    }
}
