// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Embeddings;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands;

/// <summary>Takes up the embedding model a deployment declares, after saying what doing so will cost.</summary>
/// <remarks>
/// <para>
/// The command takes no model, no provider, and no width:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes configuration the place a vector space is declared and reviewed, and leaves this the act that materializes the
/// declaration. Editing a file costs nothing; running this is the first thing MailFathom does that costs money per unit
/// of mail.
/// </para>
/// <para>
/// Which is why it reads the estimate before it asks. The deployment counts the passages it would send and weighs them
/// against the ceiling the same configuration declares, and this command puts both numbers in front of the person about
/// to agree to them. The confirmation is the default and the flag is the exception, rather than the other way round.
/// </para>
/// </remarks>
internal static class ActivateEmbeddingCommand
{
    /// <summary>Builds the <c>embedding activate</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var confirmedOption = new Option<bool>("--yes", "-y")
        {
            Description = "Agree to the estimated cost without being asked, which is what a scripted activation needs.",
        };

        Command command = new(
            "activate",
            "Take up the declared embedding model, after reporting what embedding the mailbox will cost.")
        {
            endpointOption,
            confirmedOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var client = new AdminApiClient(transport, context.Console);

        var assessment = await client.ReadEmbeddingActivationAsync(profile.Token, cancellationToken);

        Report(context, assessment);

        if (assessment.ExceedsSpendCeiling)
        {
            throw new CliFailure(DescribeCeilingRefusal(assessment));
        }

        // Reported on standard error and with a failing code, which is what every other command does when it did not
        // do what it was asked. A caller that redirected the output reads an empty result and a reason rather than a
        // sentence about nothing happening mixed into what it captured.
        if (assessment.WouldSpend && !Agreed(context, assessment, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was activated.");

            return CliExitCode.Failure;
        }

        var activation = await client.ActivateEmbeddingProfileAsync(profile.Token, cancellationToken);

        context.Console.WriteLine(activation.Describe());

        return CliExitCode.Success;
    }

    /// <summary>Writes what the deployment declared, what activating it would do, and what that would cost.</summary>
    /// <remarks>Written before anything is asked and before anything is refused, so the estimate is on the screen whichever of the three follows.</remarks>
    private static void Report(CliContext context, EmbeddingActivationAssessment assessment)
    {
        CliDetails details = new();
        details.Add("Declared", assessment.Declared?.Describe() ?? "nothing");
        details.Add("Forecast", assessment.DescribeForecast());

        if (assessment.Estimate is { } estimate)
        {
            details.Add(
                "Estimate",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{estimate.OutstandingPassageCount:N0} passages to send ({estimate.DescribeCost()})."));
        }

        if (assessment.Spend is { } spend)
        {
            details.Add("Spend", spend.Describe());
        }

        context.Console.Write(details);
    }

    /// <summary>Reports whether the person running this agreed to the cost, refusing to guess where nobody can answer.</summary>
    /// <remarks>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray line
    /// into an agreement to a provider bill. Such an invocation is told to pass the flag instead, which is an operator
    /// stating the agreement in the command rather than a command inferring it.
    /// </remarks>
    private static bool Agreed(CliContext context, EmbeddingActivationAssessment assessment, bool confirmedUpFront)
    {
        if (confirmedUpFront)
        {
            return true;
        }

        if (!context.Console.CanConfirm)
        {
            throw new CliFailure(
                $"This would spend {assessment.Estimate?.DescribeCost() ?? "an unreported amount"} at your provider, and there is nobody at the terminal to agree to it. Pass --yes to activate without being asked.");
        }

        return context.Console.Confirm("Embed the mailbox under that model? [y/N] ");
    }

    /// <summary>States the two numbers the deployment refused the activation as, before it is asked to refuse again.</summary>
    /// <remarks>
    /// Refused here rather than by sending the request and repeating what came back, because asking somebody to confirm
    /// a spend the deployment has already said it will not permit is asking a question with no answer that works.
    /// </remarks>
    private static string DescribeCeilingRefusal(EmbeddingActivationAssessment assessment)
    {
        var estimate = assessment.Estimate?.OutstandingCharacterCount;
        var ceiling = assessment.Spend?.CeilingInputCharacterCount;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"The deployment's ceiling refuses this activation: it would send {estimate:N0} characters and at most {ceiling:N0} are admitted in one period. Raise 'Embeddings:MaxInputCharactersPerPeriod', or set it to zero to declare no ceiling at all, and activate again.");
    }
}
