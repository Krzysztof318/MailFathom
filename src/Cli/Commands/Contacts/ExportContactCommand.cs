// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Text.Json;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Produces everything the deployment holds about one person, as of the instant it was taken.</summary>
/// <remarks>
/// <para>
/// The data-subject access path, which is why it is a command rather than something an operator assembles out of a
/// listing: a seam nothing invokes is a seam that will not work when somebody asks for it, and what is handed to the
/// person who asked has to be the deployment's own complete answer rather than one surface's summary of it.
/// </para>
/// <para>
/// It is written as JSON on standard output, indented. That is one document a person reads and a tool parses, and it is
/// what redirects into a file an owner can send. Everything else this command prints goes to standard error, so what is
/// captured is the export and nothing beside it.
/// </para>
/// </remarks>
internal static class ExportContactCommand
{
    /// <summary>Builds the <c>contact export</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var identityOption = ContactOptions.Identity();

        Command command = new("export", "Produce everything the deployment holds about one person, as JSON.")
        {
            identityOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(identityOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid contactId,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var export = await new AdminApiClient(transport, context.Console)
            .ExportContactAsync(profile.Token, contactId, cancellationToken);

        if (export.Contact is null)
        {
            context.Console.WriteError(
                $"The deployment's contact book holds no contact {contactId:D}, so there was nothing to export.");

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(JsonSerializer.Serialize(export, CliJsonContext.Default.ContactExport));

        return CliExitCode.Success;
    }
}
