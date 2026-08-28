// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Replaces the label an administrator tells one owner apart by.</summary>
/// <remarks>
/// <para>
/// A label is the operator's own text and the identity is not: no mail account, stored message, or job hangs on it, so
/// this changes what a roster reads like and nothing else. It asks for no confirmation for that reason, unlike the two
/// commands beside it that move a decision out of a file or destroy mail.
/// </para>
/// <para>
/// It reaches an owner nothing declares. A file that names an owner names their label too, and a start puts that label
/// back, so renaming a declared owner here lasts until the next restart — the declaration is where their label is
/// changed, and the deployment says so rather than this command guessing which kind of owner it was handed.
/// </para>
/// </remarks>
internal static class RenameOwnerCommand
{
    /// <summary>Builds the <c>owner rename</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();

        Option<string> displayNameOption = new("--display-name")
        {
            Description = "The label this owner is told apart by from now on, unique across the deployment.",
            Required = true,
        };

        Command command = new("rename", "Replace the label one owner is told apart by.")
        {
            ownerOption,
            displayNameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ownerOption),
            result.GetValue(displayNameOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedOwner,
        string displayName,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var owner = await OwnerOptions.ResolveOwnerAsync(
            deployment,
            profile.Token,
            requestedOwner,
            cancellationToken);

        await deployment.RelabelOwnerAsync(
            profile.Token,
            owner,
            new OwnerRelabelRequest(displayName),
            cancellationToken);

        context.Console.WriteLine($"Owner {owner:D} is now labelled {displayName}.");

        return CliExitCode.Success;
    }
}
