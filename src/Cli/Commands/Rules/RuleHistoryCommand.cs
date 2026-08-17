// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Reports what a deployment's rules concluded about one account's mail, and what those conclusions asked for.</summary>
/// <remarks>
/// <para>
/// The two questions it answers are the two ways an operator arrives at it. Narrowing to a message answers "why is this
/// message here"; narrowing to a rule answers "what is this rule doing", including the case where the answer is that it
/// is evaluated constantly and never matches. Naming neither reads the account's history newest first.
/// </para>
/// <para>
/// The facts are printed by name and never by value, because that is how they are recorded: what a condition compared
/// is retrievable from the rule set revision printed beside them, so the reasoning is reconstructible without a subject
/// or an address ever leaving the mailbox.
/// </para>
/// </remarks>
internal static class RuleHistoryCommand
{
    /// <summary>Builds the <c>rules history</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Option<string?> ruleOption = new("--rule")
        {
            Description = "Report only what this rule concluded. Defaults to every rule of the account.",
        };

        Option<Guid?> emailOption = new("--email")
        {
            Description = "Report only what was concluded about this message, named by its local identifier.",
        };

        Option<int?> pageSizeOption = new("--page-size")
        {
            Description = "How many executions to read. Defaults to what the deployment serves.",
        };

        Option<string?> cursorOption = new("--cursor")
        {
            Description = "Continue from where a previous page ended, using the cursor it printed.",
        };

        Command command = new("history", "Report what the rules concluded about an account's mail, newest first.")
        {
            accountOption,
            ruleOption,
            emailOption,
            pageSizeOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new MailRuleHistoryQuery(
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(ruleOption),
                result.GetValue(emailOption),
                result.GetValue(pageSizeOption),
                result.GetValue(cursorOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        MailRuleHistoryQuery query,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var page = await new AdminApiClient(transport, context.Console)
            .ReadRuleHistoryAsync(profile.Token, query, cancellationToken);

        if (page.Executions is not { Count: > 0 } executions)
        {
            context.Console.WriteLine(DescribeEmptyHistory(query));

            return CliExitCode.Success;
        }

        CliTable listing = new("Evaluated", "Rule", "Outcome", "Message", "Rule set", "Read", "Asked");

        foreach (var execution in executions)
        {
            listing.AddRow(
                $"{execution.EvaluatedAt:u}",
                execution.Rule ?? "an unnamed rule",
                execution.DescribeOutcome(),
                $"{execution.Email}",
                $"{execution.Revision} ({execution.Trigger}, {execution.DescribeDuration()})",
                execution.DescribeReadFacts(),
                execution.DescribeActions());
        }

        context.Console.Write(listing);

        if (page.NextCursor is { Length: > 0 } cursor)
        {
            context.Console.WriteLine(string.Empty);
            context.Console.WriteLine($"More executions follow. Continue with --cursor {cursor}");
        }

        return CliExitCode.Success;
    }

    /// <summary>States that the history holds nothing for these filters, and what each absence usually means.</summary>
    /// <remarks>
    /// A rule with no executions at all is worth telling apart from one that never matches, because they have different
    /// causes: the first is a rule nothing reaches — misspelled scope, or a rule below one that ends the pass — and the
    /// second is a condition that is simply never true.
    /// </remarks>
    private static string DescribeEmptyHistory(MailRuleHistoryQuery query) => query switch
    {
        { Rule: { Length: > 0 } rule } =>
            $"No rule named '{rule}' has been evaluated over {query.Account} within the retained history. Either nothing reaches it — check its scope, and whether a rule above it ends the pass — or the history it left has aged out.",
        { Email: { } email } =>
            $"No rule has been evaluated over message {email} within the retained history.",
        _ =>
            $"The retained rule history for {query.Account} is empty. Rules record what they concluded as mail is evaluated, so an account whose mail arrived before its rules did has nothing here until a run is asked for.",
    };
}
