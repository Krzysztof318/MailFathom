// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Content;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Content;

/// <summary>Frees the database copies the move left beside the objects it verified.</summary>
/// <remarks>
/// <para>
/// The move copies and never removes, so a deployment part way through one holds its mail twice: the object it now reads
/// from, and the payload the database went on holding so that a read still works while the bucket is being trusted for
/// the first time. This ends that, and it is the only irreversible thing in the whole move — what it removes is the last
/// copy of a message outside the endpoint, and nothing but a backup brings it back.
/// </para>
/// <para>
/// It is refused outright while the database still owns a payload the move has not carried, because such a payload is
/// one no object was ever verified for. Finish the move first; the refusal says so and names how much is left.
/// </para>
/// <para>
/// The copies are freed in bounded batches and the command sends as many as the deployment needs. Interrupting it stops
/// it between batches rather than part way through one: what a batch freed stays freed and the rest waits for the next
/// invocation, so there is no state this command can leave a deployment in that running it again does not finish.
/// </para>
/// </remarks>
internal static class ReleaseContentCommand
{
    /// <summary>Builds the <c>content release</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var confirmedOption = CliOptions.Confirmed("release");

        Command command = new(
            "release",
            "Free the database copies the move left beside the objects it verified, which cannot be undone.")
        {
            endpointOption,
            confirmedOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
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
        var deployment = new AdminApiClient(transport, context.Console);

        var report = await deployment.ReadContentReleaseAsync(profile.Token, cancellationToken);

        CliDetails standing = new();
        standing.Add("Retained", report.DescribeRetained());

        if (report.AwaitingMovePayloadCount > 0)
        {
            standing.Add("Awaiting the move", DescribePayloads(report.AwaitingMovePayloadCount));
        }

        context.Console.Write(standing);

        if (report.AwaitingMovePayloadCount > 0)
        {
            context.Console.WriteError(
                $"The deployment still holds content the move has not carried, so nothing can be released yet. Finish the move with '{CliRootCommand.CommandName} content move' and ask again once '{CliRootCommand.CommandName} content move-status' reports no backlog.");

            return CliExitCode.Failure;
        }

        if (!report.PayloadsRemain)
        {
            context.Console.WriteLine(
                "The database holds no copy of anything the object backend already has, so there is nothing to release.");

            return CliExitCode.Success;
        }

        // Reported on standard error and with a failing code, which is what every other command does when it did not do
        // what it was asked. A caller that redirected the output reads an empty result and a reason rather than a
        // sentence about nothing happening mixed into what it captured.
        if (!Agreed(context, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was released.");

            return CliExitCode.Failure;
        }

        return await ReleaseEveryBatchAsync(context, deployment, profile.Token, cancellationToken);
    }

    /// <summary>Sends batches until the deployment retains nothing, or answers with a batch that freed nothing.</summary>
    /// <remarks>
    /// A batch that freed nothing while copies remain is not a reason to ask again: the deployment would answer the same
    /// way, because what is left is held by the configured safety interval rather than by anything a further request
    /// changes. The command says so and stops, which is the one thing that could help.
    /// </remarks>
    private static async Task<int> ReleaseEveryBatchAsync(
        CliContext context,
        AdminApiClient deployment,
        string token,
        CancellationToken cancellationToken)
    {
        var freedPayloadCount = 0L;
        var freedByteCount = 0L;
        ContentReleaseReport batch;

        try
        {
            do
            {
                batch = await deployment.ReleaseContentAsync(token, cancellationToken);

                freedPayloadCount += batch.ReleasedPayloadCount;
                freedByteCount += batch.ReleasedByteCount;

                if (batch is { ReleasedPayloadCount: 0, PayloadsRemain: true })
                {
                    context.Console.Write(Freed(freedPayloadCount, freedByteCount));
                    context.Console.WriteLine(
                        $"{batch.DescribeRetained()} are still held and this batch freed none of them, so the configured ContentStorage:Release:SafetyInterval has not elapsed for them. Run the command again once it has.");

                    return CliExitCode.Success;
                }

                if (batch.PayloadsRemain)
                {
                    context.Console.WriteLine($"{DescribePayloads(freedPayloadCount)} released so far.");
                }
            }
            while (batch.PayloadsRemain);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Console.Write(Freed(freedPayloadCount, freedByteCount));
            context.Console.WriteError(
                "The release was interrupted. What was freed stays freed, and the rest is still there; run the command again to continue.");

            return CliExitCode.Failure;
        }

        context.Console.Write(Freed(freedPayloadCount, freedByteCount));
        context.Console.WriteLine(
            "The object backend now holds the only copy of that content. PostgreSQL reclaims the space on its own schedule, so what falls immediately is what a new backup has to carry rather than what the volume reports.");

        return CliExitCode.Success;
    }

    /// <summary>Describes what the whole release has freed so far.</summary>
    private static CliDetails Freed(long payloadCount, long byteCount)
    {
        CliDetails details = new();
        details.Add(
            "Released",
            string.Create(CultureInfo.InvariantCulture, $"{payloadCount:N0} payloads carrying {byteCount:N0} bytes"));

        return details;
    }

    /// <summary>Names a count of payloads, grouped invariantly for the reason every other figure this tool prints is.</summary>
    private static string DescribePayloads(long payloadCount) =>
        string.Create(CultureInfo.InvariantCulture, $"{payloadCount:N0} payloads");

    /// <summary>Reports whether the person running this agreed to the release, refusing to guess where nobody can answer.</summary>
    private static bool Agreed(CliContext context, bool confirmedUpFront) => CliConfirmation.Agreed(
        context,
        confirmedUpFront,
        "There is nobody at the terminal to agree to this, and releasing removes the last copy of that mail outside the object backend. Pass --yes to release without being asked.",
        "Free those copies, leaving the object backend the only place that mail is held? [y/N] ");
}
