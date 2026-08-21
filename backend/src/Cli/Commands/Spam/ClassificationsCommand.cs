// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Spam;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Spam;

/// <summary>Reports what a deployment concluded about one account's mail, and what those conclusions asked for.</summary>
/// <remarks>
/// <para>
/// The three questions it answers are the three ways an operator arrives at it. Narrowing to a message answers "why did
/// this end up in junk"; narrowing to a verdict answers "what would this run file, before I let it"; naming neither
/// reads the account's classifications newest first.
/// </para>
/// <para>
/// The signals are printed by name and never by value, because that is how they are recorded: what a header said is a
/// sending domain or an authentication result, and neither belongs in a terminal that is answering a question about a
/// decision. A change a verdict asked for is named beside the record that carries it, which is where the mutation audit
/// trail answers what became of it.
/// </para>
/// </remarks>
internal static class ClassificationsCommand
{
    /// <summary>Builds the <c>spam classifications</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Option<Guid?> emailOption = new("--email")
        {
            Description = "Report only what was concluded about this message, named by its local identifier.",
        };

        Option<string?> verdictOption = new("--verdict")
        {
            Description = "Report only messages carrying this verdict: Spam, NotSpam, or Undetermined.",
        };

        var pageSizeOption = CliOptions.PageSize("classifications");
        var cursorOption = CliOptions.Cursor();

        Command command = new(
            "classifications",
            "Report what classification concluded about an account's mail, newest first.")
        {
            accountOption,
            emailOption,
            verdictOption,
            pageSizeOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new SpamClassificationQuery(
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(emailOption),
                result.GetValue(verdictOption),
                result.GetValue(pageSizeOption),
                result.GetValue(cursorOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        SpamClassificationQuery query,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var page = await new AdminApiClient(transport, context.Console)
            .ReadSpamClassificationsAsync(profile.Token, query, cancellationToken);

        if (page.Classifications is not { Count: > 0 } classifications)
        {
            context.Console.WriteLine(DescribeEmptyReading(query));

            return CliExitCode.Success;
        }

        CliTable listing = new("Evaluated", "Verdict", "Message", "Folder", "Under", "Signals", "Asked");

        foreach (var classification in classifications)
        {
            listing.AddRow(
                $"{classification.EvaluatedAt:u}",
                classification.DescribeVerdict(),
                $"{classification.Email}",
                classification.Folder ?? "an unnamed folder",
                $"{classification.Profile ?? "no profile"}{DescribeCorpus(classification)}",
                DescribeSignals(classification),
                classification.DescribeRequestedMutations());
        }

        context.Console.Write(listing);

        if (page.NextCursor is { Length: > 0 } cursor)
        {
            context.Console.WriteLine(string.Empty);
            context.Console.WriteLine($"More classifications follow. Continue with --cursor {cursor}");
        }

        return CliExitCode.Success;
    }

    private static string DescribeCorpus(SpamClassificationReading classification) =>
        classification.CorpusRevision is { Length: > 0 } corpus ? $", scanner corpus {corpus}" : string.Empty;

    private static string DescribeSignals(SpamClassificationReading classification) =>
        classification.Signals is { Count: > 0 } signals ? string.Join(", ", signals) : "none";

    /// <summary>States that nothing was found for these filters, and what each absence usually means.</summary>
    private static string DescribeEmptyReading(SpamClassificationQuery query) => query switch
    {
        { Email: { } email } =>
            $"Message {email} carries no classification. Either it has not been classified yet, or it is outside the folders this deployment classifies.",
        { Verdict: { Length: > 0 } verdict } =>
            $"No message of {query.Account} carries the verdict '{verdict}'.",
        _ =>
            $"Nothing has been classified for {query.Account}. Classification is off unless the deployment switched it on, and mail already in the mailbox is reached by '{CliRootCommand.CommandName} spam run --account {query.Account}'.",
    };
}
