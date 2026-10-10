// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Explains why one user holds each permission they hold, and what each of their credentials keeps of it.</summary>
/// <remarks>
/// <para>
/// The answer to "why can this person do that": one row per permission, role, and scope, naming the assignment that
/// revokes it and the group it arrives through where it does not name the user directly. A permission held at a scope
/// it cannot act on — one whose every operation is the deployment's alone, given at an organization — is listed and
/// marked rather than left out, because it is still recorded and still what an operator has to revoke.
/// </para>
/// <para>
/// The credentials follow, because a request holds what its credential keeps of the user's grant rather than the
/// grant itself, and the two together are what a refusal is read against.
/// </para>
/// </remarks>
internal static class ExplainUserPermissionsCommand
{
    /// <summary>Builds the <c>user permissions</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Command command = new("permissions", "Explain why one user holds each permission they hold, credential by credential.")
        {
            userOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
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

        var explanation = await deployment.ReadUserPermissionsAsync(profile.Token, user, cancellationToken);

        if (explanation.Sources is { Count: > 0 } sources)
        {
            context.Console.Write(Draw(sources));

            if (explanation.SourcesTruncated)
            {
                context.Console.WriteLine(
                    $"User {user:D} holds more rows than one explanation carries, so the listing above and what each credential "
                    + "is shown to hold below are a part of the answer. 'assignment list' pages every assignment.");
            }
        }
        else
        {
            context.Console.WriteLine(
                $"User {user:D} holds no permission your own grant covers: no role is given to them, or to a group they "
                + "belong to, at a scope you administer.");
        }

        context.Console.WriteLine(string.Empty);

        if (explanation.Credentials is { Count: > 0 } credentials)
        {
            UserCredentialOutput.WriteListing(context.Console, credentials);
        }
        else
        {
            context.Console.WriteLine(
                $"User {user:D} holds no credentials, so no request acts with this grant until 'credential create' writes one.");
        }

        return CliExitCode.Success;
    }

    private static CliTable Draw(IReadOnlyList<GrantSource> sources)
    {
        CliTable listing = new("Permission", "Role", "Through", "Scope", "Assignment", "Effect");

        foreach (var source in sources)
        {
            listing.AddRow(
                ConsoleSafeText.Sanitize(source.Permission) ?? "unreported",
                ConsoleSafeText.Sanitize(source.Role) ?? $"{source.RoleId:D}",
                source.GroupId is { } group
                    ? $"group {ConsoleSafeText.Sanitize(source.Group) ?? $"{group:D}"}"
                    : "directly",
                source.Scope?.Describe() ?? "unreported",
                $"{source.AssignmentId:D}",
                source.Inert ? "reaches nothing at this scope" : "held");
        }

        return listing;
    }
}
