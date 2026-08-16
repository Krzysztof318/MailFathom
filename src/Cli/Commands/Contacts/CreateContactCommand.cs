// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Contacts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Records a person the deployment's contact book does not yet hold.</summary>
/// <remarks>
/// A contact written from here is asserted: somebody wrote this person down. That is what distinguishes it from a record
/// the deployment collected out of arriving mail, and it is what makes it amendable from here afterwards.
/// </remarks>
internal static class CreateContactCommand
{
    /// <summary>Builds the <c>contact create</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Option<string> nameOption = new("--name")
        {
            Description = "The name to record for the person, as you want to read it back.",
            Required = true,
        };

        Option<string[]> addressOption = new("--address")
        {
            Description = "An address the person uses. Repeat it for every address they use.",
            Required = true,
            AllowMultipleArgumentsPerToken = false,
        };

        Option<string?> preferredOption = new("--preferred")
        {
            Description =
                "Which address to use by default. Required once more than one --address is given, because which one it is is your choice rather than an ordering accident.",
        };

        Option<string?> noteOption = new("--note")
        {
            Description = "What you want recorded about the person beyond their name and addresses.",
        };

        Command command = new("create", "Record a person in the deployment's contact book.")
        {
            nameOption,
            addressOption,
            preferredOption,
            noteOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(nameOption) ?? string.Empty,
            result.GetValue(addressOption) ?? [],
            result.GetValue(preferredOption),
            result.GetValue(noteOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string name,
        IReadOnlyList<string> addresses,
        string? preferred,
        string? note,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var record = new ContactRecordRequest(name, addresses, ChoosePreferred(addresses, preferred), note);

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var written = await new AdminApiClient(transport, context.Console)
            .RecordContactAsync(profile.Token, record, cancellationToken);

        return ContactOutput.ReportWrite(context, written, "Recorded");
    }

    /// <summary>Reports which address the record is to prefer, refusing to pick one where the operator has a choice.</summary>
    /// <remarks>
    /// One address is no choice at all, so it is taken as the preferred one. Several are a decision that belongs to the
    /// person keeping the book: picking the first would make the order the arguments happened to be typed in decide
    /// which address a message to that person goes to.
    /// </remarks>
    private static string ChoosePreferred(IReadOnlyList<string> addresses, string? preferred)
    {
        if (preferred is { Length: > 0 } named)
        {
            return named;
        }

        return addresses.Count == 1
            ? addresses[0]
            : throw new CliFailure(
                "Which address is preferred is your choice rather than an ordering accident, so pass --preferred naming one of the addresses you gave.");
    }
}
