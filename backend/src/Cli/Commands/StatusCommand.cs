// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Common;

namespace MailFathom.Cli.Commands;

/// <summary>Reports what the deployment in use says about the stored credential.</summary>
/// <remarks>
/// <para>
/// It asks the deployment rather than reading the store, which is the point: the store says what was true at sign-in,
/// and this says whether the credential still works. It is therefore the command that tells an operator their key has
/// been revoked or has expired.
/// </para>
/// <para>
/// It is also where an operator learns where to read about what they are administering, and the version that decides
/// that is the deployment's rather than this command's: the two are separate builds, and a command from a nightly
/// pointed at a released deployment would otherwise name pages for something nobody is running. A deployment that
/// reports no version it can read is told nothing about documentation, which is the same absence of evidence
/// <see cref="DeploymentVersionAgreement" /> warns on rather than acts on.
/// </para>
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
        var session = await new AdminApiClient(transport, context.Console).ReadSessionAsync(profile.Token, cancellationToken);

        context.Console.WriteLine(
            $"'{profile.Name}' ({profile.Endpoint.GetLeftPart(UriPartial.Authority)}) accepts the stored credential as '{session.Credential}' (MailFathom {session.Version}).");

        context.Console.WriteLine(DescribeGrant(session.Permissions));

        if (DocumentationAddress.ForVersion(session.Version) is { } documentation)
        {
            context.Console.WriteLine($"Documentation for that version: {documentation}");
        }

        return CliExitCode.Success;
    }

    /// <summary>States what the credential may do, which is what decides whether any other command will work.</summary>
    /// <remarks>
    /// Reported here rather than left to be discovered one refusal at a time: an operator who has just signed in wants
    /// to know which commands are theirs before they run one. A credential granted nothing is the case worth stating
    /// plainly, because it is how one is retired without its entry being deleted and its sign-in still succeeds.
    /// </remarks>
    private static string DescribeGrant(IReadOnlyList<string>? permissions) => permissions switch
    {
        null => "The deployment did not state what the credential may do.",
        { Count: 0 } => "It holds no administrative permission, so every operation but this one is refused.",
        _ => $"It holds {string.Join(", ", permissions)}.",
    };
}
