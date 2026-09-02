// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands;

/// <summary>Drops an account's synchronization progress so its folders are read from the server afresh.</summary>
/// <remarks>
/// <para>
/// The expensive way to fill in properties a newer release added, and the only way for a property the mail server
/// alone knows: flags, keywords, the internal date. A run resumes from the UID its folder's checkpoint records, so
/// mail already mirrored is never asked about again — and taking that record away is what makes the next runs read it.
/// <c>mfctl mailbox rederive</c> is the cheap answer wherever the property is already in the MIME this deployment
/// stored.
/// </para>
/// <para>
/// Which is why it reads the cost before it asks. The deployment counts what the scope holds and this command puts
/// that figure in front of the person about to agree to a mailbox coming over IMAP again, exactly as
/// <c>mfctl embedding activate</c> does with a provider bill. The confirmation is the default and the flag is the
/// exception, rather than the other way round.
/// </para>
/// <para>
/// It erases nothing. What it removes is one row of progress per folder binding, and the mail, its raw MIME, its
/// passages, and their vectors all stay exactly where they are — a re-read stores over the local email it already
/// has rather than storing a second one.
/// </para>
/// </remarks>
internal static class RewindMailboxCommand
{
    /// <summary>Builds the <c>mailbox rewind</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var folderOption = CliOptions.NarrowedMailFolder();
        var confirmedOption = CliOptions.Confirmed("fetch");

        Command command = new(
            "rewind",
            "Drop synchronization progress so the next runs read the account's folders from the server again.")
        {
            accountOption,
            folderOption,
            endpointOption,
            confirmedOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(folderOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? folder,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var assessment = await deployment.ReadMailboxRewindAsync(profile.Token, account, folder, cancellationToken);

        CliDetails scope = new();
        scope.Add("Scope", Describe(account, folder));
        scope.Add("Cost", assessment.DescribeCost());

        context.Console.Write(scope);

        // Reported on standard error and with a failing code, which is what every other command does when it did not
        // do what it was asked. A caller that redirected the output reads an empty result and a reason rather than a
        // sentence about nothing happening mixed into what it captured.
        if (!Agreed(context, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was rewound.");

            return CliExitCode.Failure;
        }

        var rewind = await deployment.RewindMailboxAsync(profile.Token, account, folder, cancellationToken);
        var folders = rewind.Folders ?? [];

        if (folders.Count == 0)
        {
            context.Console.WriteLine(
                "No folder of that scope had any synchronization progress, so there was nothing to rewind. Its next run reads from the start of the account's window either way.");

            return CliExitCode.Success;
        }

        CliDetails rewound = new();
        rewound.Add("Rewound", folders);

        context.Console.Write(rewound);

        context.Console.WriteLine(
            "Each reads from the first UID inside the account's synchronization window on its next run. Nothing was erased, and a run already under way is refused its next advance rather than corrupting this.");

        return CliExitCode.Success;
    }

    /// <summary>Names the scope the way the operator wrote it, because that is what they are agreeing about.</summary>
    private static string Describe(string account, string? folder) => folder is { Length: > 0 } narrowed
        ? $"{narrowed} under {account}"
        : $"every folder under {account}";

    /// <summary>Reports whether the person running this agreed to the fetch, refusing to guess where nobody can answer.</summary>
    /// <remarks>
    /// A scope the assessment counted no mail in is asked about like any other. The count is what the deployment
    /// stores, which is deliberately not what a run would fetch: mail that arrived since is fetched without ever
    /// having been stored, and a scope whose local copies are all tombstoned counts nothing while its folders still
    /// hold progress a rewind takes away. Both are cases where zero would have waved through the fetch this
    /// confirmation exists for, and the second needs no race to reach. So the figure informs the question rather than
    /// answering it, and <c>--yes</c> stays the one way to state the agreement without being asked.
    /// </remarks>
    private static bool Agreed(CliContext context, bool confirmedUpFront) => CliConfirmation.Agreed(
        context,
        confirmedUpFront,
        "There is nobody at the terminal to agree to this, and rewinding has the deployment read the scope from the mail server again. Pass --yes to rewind without being asked.",
        "Rewind that scope? [y/N] ");
}
