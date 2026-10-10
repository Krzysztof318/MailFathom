// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;

namespace MailFathom.Cli.Commands.RoleAssignments;

/// <summary>Gives a role to a user or a group at a scope.</summary>
/// <remarks>
/// <para>
/// The scope is stated rather than defaulted: a missing scope must never read as the deployment, which is the widest
/// grant there is, so <c>--deployment</c> is a flag of its own and an invocation naming no scope is refused before a
/// request is made. The same holds for who is given the role — exactly one of a user and a group.
/// </para>
/// <para>
/// The user a role is scoped to is <c>--scope-user</c> rather than a second <c>--user</c>, because the two answer
/// different questions: who holds the role, and whose records it reaches.
/// </para>
/// </remarks>
internal static class AddRoleAssignmentCommand
{
    private const string User = "user";

    private const string Group = "group";

    private const string Deployment = "deployment";

    private const string Organization = "organization";

    /// <summary>Builds the <c>assignment add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Option<Guid> roleOption = new("--role")
        {
            Description = "The role to give, by the identifier 'role list' reports.",
            Required = true,
        };

        Option<Guid?> userOption = new("--user")
        {
            Description = "The user the role is given to, by the identifier 'user list' reports. Refused beside '--group'.",
        };

        Option<Guid?> groupOption = new("--group")
        {
            Description = "The group the role is given to, by the identifier 'group list' reports. Refused beside '--user'.",
        };

        Option<bool> deploymentOption = new("--deployment")
        {
            Description = "The role reaches the whole deployment. Refused beside '--organization' and '--scope-user'.",
        };

        Option<Guid?> organizationOption = new("--organization")
        {
            Description = "The role reaches one organization, by the identifier 'organization list' reports.",
        };

        Option<Guid?> scopeUserOption = new("--scope-user")
        {
            Description = "The role reaches one user's records, by the identifier 'user list' reports.",
        };

        Command command = new("add", "Give a role to a user or a group at a scope.")
        {
            roleOption,
            userOption,
            groupOption,
            deploymentOption,
            organizationOption,
            scopeUserOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) =>
        {
            var (principalKind, principalId) = ResolvePrincipal(result.GetValue(userOption), result.GetValue(groupOption));
            var (scopeKind, scopeId) = ResolveScope(
                result.GetValue(deploymentOption),
                result.GetValue(organizationOption),
                result.GetValue(scopeUserOption));

            return RunAsync(
                context,
                new RoleAssignmentRequest(result.GetValue(roleOption), principalKind, principalId, scopeKind, scopeId),
                CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
                cancellationToken);
        });

        return command;
    }

    /// <summary>Settles on who the role is given to, refusing an invocation that named both or neither.</summary>
    private static (string Kind, Guid Id) ResolvePrincipal(Guid? user, Guid? group) => (user, group) switch
    {
        ({ } named, null) => (User, named),
        (null, { } named) => (Group, named),
        _ => throw new CliFailure(
            "Name exactly one of '--user' and '--group' to say who the role is given to."),
    };

    /// <summary>Settles on what the role reaches, refusing an invocation that named several scopes or none.</summary>
    private static (string Kind, Guid? Id) ResolveScope(bool deployment, Guid? organization, Guid? user) =>
        (deployment, organization, user) switch
        {
            (true, null, null) => (Deployment, null),
            (false, { } named, null) => (Organization, named),
            (false, null, { } named) => (User, named),
            _ => throw new CliFailure(
                "Name exactly one scope: '--deployment' for the whole deployment, '--organization' for one "
                + "organization, or '--scope-user' for one user's records."),
        };

    private static async Task<int> RunAsync(
        CliContext context,
        RoleAssignmentRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var recorded = await deployment.AssignRoleAsync(profile.Token, request, cancellationToken);

        var scope = new GrantReference(request.ScopeKind, request.ScopeId).Describe();

        context.Console.WriteLine(
            $"Gave role {request.RoleId:D} to {request.PrincipalKind} {request.PrincipalId:D} at {scope} as assignment "
            + $"{recorded.Id:D}.");
        context.Console.WriteNotice(
            "It reaches them from their next request. Revoke it with 'mfctl assignment revoke'.");

        return CliExitCode.Success;
    }
}
