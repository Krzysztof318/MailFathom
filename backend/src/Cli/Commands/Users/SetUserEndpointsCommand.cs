// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Keeps one user off the MCP endpoint, the client endpoint, or both, or lets them back on.</summary>
/// <remarks>
/// <para>
/// The switch is the user's rather than a credential's, so it holds whichever credential they present, and it reaches
/// what they already hold: a session or a token is refused on its next request rather than when it expires. Nothing is
/// deleted, so turning a switch back on serves them again with what they still have — which is why this asks for no
/// confirmation, unlike disabling a credential for good or erasing somebody.
/// </para>
/// <para>
/// Either option may be left out and the deployment leaves that switch where it stands, so keeping a person off one
/// endpoint never rewrites the other.
/// </para>
/// </remarks>
internal static class SetUserEndpointsCommand
{
    /// <summary>Builds the <c>user endpoints</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<bool?> mcpOption = new("--mcp")
        {
            Description = "Whether this user is served on the MCP endpoint: true or false. Left out, it stays as it is.",
        };

        Option<bool?> clientOption = new("--client")
        {
            Description = "Whether this user is served on the client endpoint: true or false. Left out, it stays as it is.",
        };

        Command command = new("endpoints", "Keep one user off the MCP endpoint, the client endpoint, or both, or let them back on.")
        {
            userOption,
            mcpOption,
            clientOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(mcpOption),
            result.GetValue(clientOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        bool? mcpEndpoint,
        bool? clientEndpoint,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        // Refused before anything is reached, because a command naming neither switch would change nothing and still
        // cost a sign-in and a roster read to find that out.
        if (mcpEndpoint is null && clientEndpoint is null)
        {
            context.Console.WriteError("Name at least one switch to write: --mcp true|false, --client true|false.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);
        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(
            deployment,
            profile.Token,
            requestedUser,
            cancellationToken);

        var written = await deployment.SetUserEndpointAccessAsync(
            profile.Token,
            user,
            new UserEndpointAccessRequest(mcpEndpoint, clientEndpoint),
            cancellationToken);

        context.Console.WriteLine($"User {user:D} is served as follows from their next request, on every replica:");
        context.Console.WriteLine(UserOutput.DescribeEndpoints(written.McpEndpoint, written.ClientEndpoint));

        return CliExitCode.Success;
    }
}
