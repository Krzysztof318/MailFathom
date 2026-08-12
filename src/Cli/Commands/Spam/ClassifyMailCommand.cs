// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Spam;

namespace MailFathom.Cli.Commands.Spam;

/// <summary>Asks a deployment to classify every message it already holds for one account.</summary>
/// <remarks>
/// <para>
/// The command an operator runs after switching classification on, moving a threshold, or switching filing on. Mail is
/// classified as it arrives, so none of those three reaches the inbox that is already there until somebody asks — and
/// this is that asking.
/// </para>
/// <para>
/// <strong>It is a dry run unless <c>--apply</c> is given.</strong> With filing switched on, a run over an inbox is the
/// largest single thing this feature can do to somebody's mail, so the default reports what it would do and touches
/// nothing on the mail server. The verdicts are recorded either way, because a classification is derived data rather
/// than a change to a mailbox.
/// </para>
/// <para>
/// It returns as soon as the deployment has written the request down, and never waits for the walk. The run is carried
/// by the account's synchronization runs, so this terminal is not what keeps it alive and closing it cannot cancel one;
/// <c>spam run-status</c> is where the run is watched from.
/// </para>
/// </remarks>
internal static class ClassifyMailCommand
{
    /// <summary>Builds the <c>spam run</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Option<string[]?> foldersOption = new("--folder")
        {
            Description = "Classify only this folder, named by its alias. Repeatable. Defaults to the folders the deployment classifies.",
            AllowMultipleArgumentsPerToken = true,
        };

        Option<bool> applyOption = new("--apply")
        {
            Description = "Carry out what the switches ask for. Without it the run reports what it would do and changes nothing.",
        };

        Option<bool> rescoreOption = new("--rescore")
        {
            Description = "Score mail again even where its verdict was already reached under the settings now in force.",
        };

        Command command = new("run", "Classify every message the deployment already holds for an account.")
        {
            accountOption,
            foldersOption,
            applyOption,
            rescoreOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new SpamClassificationRunRequest(
                result.GetValue(accountOption) ?? string.Empty,
                result.GetValue(foldersOption),
                result.GetValue(applyOption),
                result.GetValue(rescoreOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        SpamClassificationRunRequest request,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var started = await new AdminApiClient(transport, context.Console)
            .StartSpamClassificationRunAsync(profile.Token, request, cancellationToken);

        context.Console.WriteLine(started.Started
            ? $"A classification run over {request.Account} has been asked for."
            : $"A classification run over {request.Account} was already under way, so nothing new was started and the terms you asked for were not applied to it.");

        if (started.Run is { } run)
        {
            context.Console.WriteLine($"Folders:  {string.Join(", ", run.Folders)}");
            context.Console.WriteLine($"Acting:   {DescribePosture(run)}");
            context.Console.WriteLine($"Progress: {run.DescribeProgress()}");
        }

        context.Console.WriteLine(
            $"The run is carried by the account's synchronization runs. Watch it with '{CliRootCommand.CommandName} spam run-status --account {request.Account}'.");

        return CliExitCode.Success;
    }

    /// <summary>States what the run will do to the mailbox, in the words an operator has to be able to act on.</summary>
    /// <remarks>
    /// A dry run says so and says how to ask for the other one, because the whole point of the default is that somebody
    /// reads the report before the mail moves.
    /// </remarks>
    private static string DescribePosture(SpamClassificationRun run) => run.IsDryRun
        ? "no — this is a dry run; it records verdicts and leaves the mailbox alone. Add --apply to carry out what the switches ask for."
        : "yes — what the deployment's switches ask for is written down and carried out by the account's convergence pass.";
}
