// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Mailboxes;

namespace MailFathom.Cli.Commands;

/// <summary>Re-reads the MIME a deployment already stores into the properties a newer release records from it.</summary>
/// <remarks>
/// <para>
/// The cheap way to fill in properties a newer release added, and the one to reach for first. Everything the stored
/// payload itself carries — the sender identity the receiving server authenticated is today's example — is already on
/// the deployment's own disk, so filling in a column costs a local read and a parse rather than a mailbox over IMAP.
/// <c>mfctl mailbox rewind</c> is the answer for a property only the mail server knows.
/// </para>
/// <para>
/// It opens no mailbox session at all, so it cannot touch a remote <c>\Seen</c> flag however long it runs, and it
/// re-chunks and re-embeds nothing: passages and vectors are derived from text that the same bytes read by the same
/// reader produce unchanged, and spending a provider bill to arrive back there is not something a metadata refresh
/// does.
/// </para>
/// <para>
/// One request is one bounded pass, and the command sends as many as the scope needs. Interrupting it stops it between
/// batches rather than part way through one: what a batch committed stays committed and the deployment remembers where
/// it got to, so running the command again continues rather than starting the scope over.
/// </para>
/// </remarks>
internal static class RederiveMailboxCommand
{
    /// <summary>Builds the <c>mailbox rederive</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var folderOption = CliOptions.NarrowedMailFolder();

        Command command = new(
            "rederive",
            "Re-read the MIME already stored into the properties a newer version records from it.")
        {
            accountOption,
            folderOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(folderOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? folder,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var rederivedCount = 0;
        var unreadableCount = 0;
        var missingContentCount = 0;
        MailboxRederivationPass? pass = null;

        try
        {
            do
            {
                pass = await deployment.RederiveMailboxAsync(profile.Token, account, folder, cancellationToken);

                rederivedCount += pass.RederivedEmailCount;
                unreadableCount += pass.UnreadableEmailCount;
                missingContentCount += pass.MissingContentEmailCount;

                // A pass that read nothing at all and reports more to come would repeat forever, because nothing about
                // the next request would differ from this one. The deployment's own walk cannot answer that — a pass
                // saying mail remains has filled its bound — so this is a deployment answering something else, and
                // asking it again is the one thing that could not help.
                if (pass is
                    {
                        RederivedEmailCount: 0,
                        UnreadableEmailCount: 0,
                        MissingContentEmailCount: 0,
                        EmailsRemain: true,
                    })
                {
                    throw new CliFailure(
                        "The deployment reported that mail remains but read none of it, so asking again would not make progress. What earlier passes wrote is still there.");
                }

                if (pass.EmailsRemain)
                {
                    context.Console.WriteLine(Describe(rederivedCount, "re-read so far"));
                }
            }
            while (pass.EmailsRemain);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Console.WriteError(
                $"{Describe(rederivedCount, "re-read")} before the re-derivation was interrupted. What was written is still there; run the command again to continue from where it stopped.");

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(rederivedCount == 0
            ? $"The deployment had no stored MIME to re-read for {Scope(account, folder)}, so nothing was re-derived."
            : $"{Describe(rederivedCount, "re-read")} for {Scope(account, folder)}.");

        WriteSteppedOver(context, unreadableCount, missingContentCount);

        return CliExitCode.Success;
    }

    /// <summary>States what the pass could not re-read, which is a fact about the mailbox rather than a failure.</summary>
    /// <remarks>
    /// The two counts stay apart because they ask the operator different questions: one is a message nobody can parse,
    /// which keeps whatever an earlier release read from it, and the other a row whose raw MIME is no longer stored,
    /// which only a fetch could bring back.
    /// </remarks>
    private static void WriteSteppedOver(CliContext context, int unreadableCount, int missingContentCount)
    {
        if (unreadableCount > 0)
        {
            context.Console.WriteLine(
                $"{Describe(unreadableCount, "carried MIME no reader could parse")} and kept what was already recorded for them.");
        }

        if (missingContentCount > 0)
        {
            context.Console.WriteLine(
                $"{Describe(missingContentCount, "no longer had stored MIME to read")}, so only 'mfctl mailbox rewind' would reach them.");
        }
    }

    /// <summary>Names the scope the way the operator wrote it.</summary>
    private static string Scope(string account, string? folder) => folder is { Length: > 0 } narrowed
        ? $"{narrowed} under {account}"
        : $"every folder under {account}";

    /// <summary>Describes a count of stored mail, grouped invariantly for the reason every other figure this tool prints is.</summary>
    private static string Describe(int count, string state) => string.Create(
        CultureInfo.InvariantCulture,
        $"{count:N0} stored {(count == 1 ? "email" : "emails")} {state}");
}
