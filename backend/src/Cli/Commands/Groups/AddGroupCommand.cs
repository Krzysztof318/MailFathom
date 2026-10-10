// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;

namespace MailFathom.Cli.Commands.Groups;

/// <summary>Records a group, in an organization or in none.</summary>
/// <remarks>
/// A group in no organization is the deployment's own, which only an administrator over the whole deployment may write,
/// so belonging to none is stated with a flag of its own rather than reached by leaving the organization out. An
/// invocation naming both or neither is refused before a request is made.
/// </remarks>
internal static class AddGroupCommand
{
    /// <summary>Builds the <c>group add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Option<string> nameOption = new("--name")
        {
            Description = "The name an operator reads this group by. Unique across every role and group.",
            Required = true,
        };

        Option<Guid?> organizationOption = new("--organization")
        {
            Description = "The organization the group belongs to, by the identifier 'organization list' reports. Refused beside '--no-organization'.",
        };

        Option<bool> noOrganizationOption = new("--no-organization")
        {
            Description = "The group belongs to no organization, which makes it the deployment's own. Refused beside '--organization'.",
        };

        Command command = new("add", "Record a group, in an organization or in none.")
        {
            nameOption,
            organizationOption,
            noOrganizationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            Resolve(
                result.GetValue(nameOption) ?? string.Empty,
                result.GetValue(organizationOption),
                result.GetValue(noOrganizationOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Settles on the organization a group is recorded in, refusing an invocation that named both or neither.</summary>
    private static GroupProvisioningRequest Resolve(string name, Guid? organization, bool none) => (organization, none) switch
    {
        ({ }, true) => throw new CliFailure(
            "The invocation both names an organization and says the group belongs to none. Drop '--no-organization' to "
            + "record it in that organization, or drop '--organization' to make it the deployment's own."),
        (null, false) => throw new CliFailure(
            "The invocation names no organization. Pass '--organization' to record the group in one, or "
            + "'--no-organization' to make it the deployment's own."),
        _ => new GroupProvisioningRequest(name, organization, none),
    };

    private static async Task<int> RunAsync(
        CliContext context,
        GroupProvisioningRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var recorded = await deployment.ProvisionGroupAsync(profile.Token, request, cancellationToken);

        context.Console.WriteLine(
            request.OrganizationId is { } organization
                ? $"Recorded group {request.Name} in organization {organization:D} as {recorded.Id:D}."
                : $"Recorded group {request.Name}, in no organization, as {recorded.Id:D}.");
        context.Console.WriteNotice(
            "Nobody belongs to it and it holds no role yet. Add members with 'mfctl group add-member' and give it a role "
            + "with 'mfctl assignment add'.");

        return CliExitCode.Success;
    }
}
