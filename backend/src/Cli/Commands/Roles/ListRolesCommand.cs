// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Grants;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Roles;

/// <summary>Reads the roles this deployment defines.</summary>
/// <remarks>
/// The listing every other role and assignment command takes a role's identifier out of, and the one place an operator
/// reads which names a role grants.
/// </remarks>
internal static class ListRolesCommand
{
    /// <summary>Builds the <c>role list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Read the roles this deployment defines.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var listing = await deployment.ReadRolesAsync(profile.Token, cancellationToken);

        if (listing.Roles is not { Count: > 0 } roles)
        {
            context.Console.WriteLine("This deployment defines no roles. Define one with 'role add'.");

            return CliExitCode.Success;
        }

        // The deployment pages in identifier order; an operator looks a role up by its name.
        context.Console.Write(Draw([.. roles.OrderBy(role => role.Name, StringComparer.Ordinal)]));

        ReportUnpublished(context, roles);

        return CliExitCode.Success;
    }

    /// <summary>Says which roles list names this deployment's build does not publish.</summary>
    /// <remarks>
    /// Printed after the listing rather than mixed into it, because those names grant nothing: a build that stopped
    /// publishing a permission leaves it stored on every role that listed it, and nothing else would tell an operator
    /// the role is narrower than it reads.
    /// </remarks>
    private static void ReportUnpublished(CliContext context, IReadOnlyList<RoleEntry> roles)
    {
        RoleEntry[] carrying = [.. roles.Where(role => role.Unpublished is { Count: > 0 })];

        if (carrying.Length == 0)
        {
            return;
        }

        context.Console.WriteLine(string.Empty);
        context.Console.WriteLine(
            $"{carrying.Length} roles list names this deployment does not publish, which grant nothing. Replace each "
            + "list with 'role set-permissions'.");

        foreach (var role in carrying)
        {
            context.Console.WriteLine(
                $"  {role.Id:D} ({ConsoleSafeText.Sanitize(role.Name) ?? "unreported"}): "
                + string.Join(", ", role.Unpublished!.Select(ConsoleSafeText.Sanitize)));
        }
    }

    private static CliTable Draw(IReadOnlyList<RoleEntry> roles)
    {
        CliTable listing = new("Role", "Name", "Permissions", "Recorded");

        foreach (var role in roles)
        {
            listing.AddRow(
                $"{role.Id:D}",
                ConsoleSafeText.Sanitize(role.Name) ?? "unreported",
                DescribePermissions(role.Permissions),
                $"{role.CreatedAt:u}");
        }

        return listing;
    }

    private static string DescribePermissions(IReadOnlyList<string>? permissions) => permissions switch
    {
        null => "unreported",
        { Count: 0 } => "nothing",
        _ => string.Join(", ", permissions.Select(ConsoleSafeText.Sanitize)),
    };
}
