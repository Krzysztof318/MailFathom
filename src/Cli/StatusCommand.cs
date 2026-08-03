// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>Reports what the deployment in use says about the stored credential.</summary>
/// <remarks>
/// It asks the deployment rather than reading the store, which is the point: the store says what was true at sign-in,
/// and this says whether the credential still works. It is therefore the command that tells an operator their key has
/// been revoked or has expired.
/// </remarks>
internal static class StatusCommand
{
    /// <summary>Builds the <c>status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("status", "Check that the stored credential still works.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = context.Store.Resolve(requestedDeployment);

        using var transport = context.OpenTransport(profile.Endpoint);
        var session = await new AdminApiClient(transport).ReadSessionAsync(profile.Token, cancellationToken);

        context.Console.WriteLine(
            $"'{profile.Name}' ({profile.Endpoint.GetLeftPart(UriPartial.Authority)}) accepts the stored credential as '{session.Credential}' (MailFathom {session.Version}).");

        return CliExitCode.Success;
    }
}
