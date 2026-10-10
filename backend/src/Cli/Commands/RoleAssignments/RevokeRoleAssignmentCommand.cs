// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.RoleAssignments;

/// <summary>Revokes one role assignment.</summary>
/// <remarks>
/// Everybody the assignment reached loses what it gave from their next request, which is why it asks first. Giving the
/// role again is a new assignment with a new identifier. The deployment refuses the one revocation that would leave
/// nobody administering it.
/// </remarks>
internal static class RevokeRoleAssignmentCommand
{
    /// <summary>Builds the <c>assignment revoke</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var confirmedOption = CliOptions.Confirmed("revocation");

        Option<Guid> assignmentOption = new("--assignment")
        {
            Description = "The assignment to revoke, by the identifier 'assignment list' reports.",
            Required = true,
        };

        Command command = new("revoke", "Revoke one role assignment, taking what it gave from everybody it reached.")
        {
            assignmentOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(assignmentOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid assignment,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and revoking an assignment takes what it gave from everybody it reached. Pass --yes to revoke without being asked.",
                $"Revoke assignment {assignment:D}? [y/N] "))
        {
            context.Console.WriteError("Nothing was revoked.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        await deployment.RevokeRoleAssignmentAsync(profile.Token, assignment, cancellationToken);

        context.Console.WriteLine($"Revoked assignment {assignment:D}.");

        return CliExitCode.Success;
    }
}
