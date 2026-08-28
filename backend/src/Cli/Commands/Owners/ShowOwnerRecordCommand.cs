// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Reads one owner's record as the deployment holds it.</summary>
/// <remarks>
/// What an administrator answers "which mailboxes does this deployment read for this person, and where is that decided"
/// from. The document arrives with every secret-bearing value replaced by the deployment's redaction marker, which is
/// what an editing session saves back unchanged to leave the reference beneath it alone. Redaction is about secrets and
/// nothing else: the mail server, the mailbox user name, the secret's declared name, and the folder aliases are printed
/// as the record holds them, and a mailbox user name is ordinarily the person's own address, so this output is one
/// person's mailbox identity rather than something to paste where the record itself would not go.
/// </remarks>
internal static class ShowOwnerRecordCommand
{
    /// <summary>Builds the <c>owner show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();

        Command command = new("show", "Read one owner's record as this deployment holds it.")
        {
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedOwner,
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

        var record = await deployment.ReadOwnerRecordAsync(profile.Token, owner, cancellationToken);

        context.Console.WriteLine($"{record.DisplayName} ({record.Owner:D})");
        context.Console.WriteLine($"  version: {record.Version.ToString(CultureInfo.InvariantCulture)}");
        context.Console.WriteLine($"  mail accounts read from: {record.Source}");
        context.Console.WriteLine(record.Document ?? "{}");

        if (record.ReadFromConfiguration)
        {
            context.Console.WriteNotice(
                "A configuration source supplies this owner's mail accounts, so their record is empty and every change "
                + "to it is refused. Run 'mfctl owner adopt' to move them into their own record first.");
        }

        return CliExitCode.Success;
    }
}
