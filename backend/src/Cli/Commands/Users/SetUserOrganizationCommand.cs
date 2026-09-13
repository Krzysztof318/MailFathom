// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Organizations;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Moves one user into an organization, or out of every organization.</summary>
/// <remarks>
/// <para>
/// An organization scopes a password login, so moving a user changes what they type: a member signs in as
/// <c>SHORTNAME/username</c> and a user in none as the username alone, from the moment the deployment answers.
/// </para>
/// <para>
/// Leaving every organization takes a word of its own, <c>--none</c>, rather than an omitted <c>--organization</c>: an
/// unset script variable would otherwise read as the decision to take somebody out of their organization. Both and
/// neither are refused before a request is made.
/// </para>
/// </remarks>
internal static class SetUserOrganizationCommand
{
    /// <summary>Builds the <c>user set-organization</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<Guid?> organizationOption = new("--organization")
        {
            Description = "The organization the user belongs to from now on, by the identifier 'organization list' reports. Refused beside '--none'.",
        };

        Option<bool> noneOption = new("--none")
        {
            Description = "Take the user out of every organization, so they sign in under their username alone. Refused beside '--organization'.",
        };

        Command command = new("set-organization", "Move one user into an organization, or out of every organization.")
        {
            userOption,
            organizationOption,
            noneOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            ResolveOrganization(result.GetValue(organizationOption), result.GetValue(noneOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Settles on the organization a move names, refusing an invocation that named both or neither.</summary>
    private static UserOrganizationRequest ResolveOrganization(Guid? organization, bool none) => (organization, none) switch
    {
        ({ }, true) => throw new CliFailure(
            "The invocation both names an organization and says the user belongs to none. Drop '--none' to move them "
            + "into it, or drop '--organization' to take them out of every organization."),
        (null, false) => throw new CliFailure(
            "The invocation names no organization. Pass '--organization' to move the user into one, or '--none' to take "
            + "them out of every organization."),
        _ => new UserOrganizationRequest(organization),
    };

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        UserOrganizationRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(
            deployment,
            profile.Token,
            requestedUser,
            cancellationToken);

        await deployment.SetUserOrganizationAsync(profile.Token, user, request, cancellationToken);

        context.Console.WriteLine(
            request.OrganizationId is { } organization
                ? $"User {user:D} now belongs to organization {organization:D}."
                : $"User {user:D} now belongs to no organization.");
        context.Console.WriteNotice(
            "Their password credentials sign in under the new login from now on, and under the previous one no longer. "
            + "Read it with 'mfctl credential list'.");

        return CliExitCode.Success;
    }
}
