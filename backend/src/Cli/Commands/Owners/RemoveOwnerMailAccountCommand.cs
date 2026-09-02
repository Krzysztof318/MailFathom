// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Stops an owner's record declaring one mailbox.</summary>
/// <remarks>
/// It withdraws the declaration and nothing else. The mail this deployment already stored for the account stays exactly
/// where it is, as it does when a configuration file stops declaring one — no configuration change takes somebody's
/// mail away, and getting rid of it is <c>mfctl folder erase</c> or <c>mfctl owner remove</c>, each of which says so.
/// So this is reversible by declaring the account again, and that is why it is not confirmed.
/// </remarks>
internal static class RemoveOwnerMailAccountCommand
{
    /// <summary>Builds the <c>owner account remove</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();

        Option<string> accountOption = new("--id")
        {
            Description = "The mail account to withdraw, by the identifier it was declared under.",
            Required = true,
        };

        Command command = new("remove", "Stop an owner's record declaring one mailbox, leaving its stored mail alone.")
        {
            ownerOption,
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ownerOption),
            result.GetValue(accountOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedOwner,
        string accountId,
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

        var answer = await deployment.RemoveOwnerMailAccountAsync(
            profile.Token,
            owner,
            new OwnerMailAccountRemovalRequest(record.Version, accountId),
            cancellationToken);

        var exitCode = OwnerOutput.ReportWrite(context, answer);

        if (answer.Committed)
        {
            context.Console.WriteNotice(
                $"The mail already stored for {accountId} was not touched. Declaring the account again resumes it; "
                + "'mfctl folder erase' is what disposes of it.");
        }

        return exitCode;
    }
}
