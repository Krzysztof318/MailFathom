// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Embeddings;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands;

/// <summary>Answers whether semantic search is working on a deployment, and how far behind it is.</summary>
/// <remarks>
/// The one command an operator runs when semantic search is not returning what they expected. It exists because that
/// question has five answers that look nothing alike — no provider declared, a declaration nobody activated, a provider
/// refusing the credential, a reindex still running, a budget period spent — and reading them out of logs means knowing
/// which of the five to look for first.
/// </remarks>
internal static class EmbeddingStatusCommand
{
    /// <summary>Builds the <c>embedding status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("status", "Report whether semantic search is working, and how far behind it is.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var status = await new AdminApiClient(transport, context.Console).ReadEmbeddingStatusAsync(profile.Token, cancellationToken);

        CliDetails details = new();
        details.Add("Deployment", $"{profile.Name} ({profile.Endpoint.GetLeftPart(UriPartial.Authority)})");
        details.Add("Declared", DescribeDeclaration(status));
        details.Add("Serving", DescribeServing(status.Serving));
        details.Add("Reindex", DescribeReindex(status.Building));
        details.Add("Next pass", DescribeNextPass(status.NextBackfillPassDueAt));
        details.Add("Provider", DescribeProvider(status.Provider));
        details.Add("Spend", status.Spend?.Describe() ?? "not reported");

        context.Console.Write(details);

        return CliExitCode.Success;
    }

    /// <summary>States what the deployment declares, and whether anything has taken that declaration up.</summary>
    /// <remarks>
    /// The outstanding case is the one this line exists for. Editing configuration declares a model and starts nothing,
    /// so an operator who changed a file and expected search results to change learns it here rather than from search
    /// results that stayed the same.
    /// </remarks>
    private static string DescribeDeclaration(EmbeddingStatus status)
    {
        if (status.Declared is not { } declared)
        {
            return "nothing. This deployment embeds nothing and answers searches lexically. Declare a provider under 'Embeddings:Endpoints' to change that.";
        }

        return status.ActivationOutstanding
            ? $"{declared.Describe()} — no activation has taken this declaration up. Run '{CliRootCommand.CommandName} embedding activate'."
            : declared.Describe();
    }

    private static string DescribeServing(EmbeddingGeneration? serving) => serving is { } present
        ? $"{present.Geometry?.Describe() ?? "an unreported model"} — {present.Progress?.DescribeProgress() ?? "progress not reported"}"
        : "none. Nothing has been activated, so searches are answered lexically.";

    private static string DescribeReindex(EmbeddingGeneration? building) => building is { } present
        ? $"{present.Geometry?.Describe() ?? "an unreported model"} — {present.Progress?.DescribeProgress() ?? "progress not reported"}"
        : "none running.";

    /// <summary>States when the walk next runs, which is what tells a deployment that is waiting from one that is failing.</summary>
    /// <remarks>
    /// The line this command was missing. A deployment between passes reports nothing served, nothing outstanding
    /// moving, and a provider nothing has been asked of — three readings an operator has no way to tell apart from a
    /// broken instance until one of them says a pass is simply not due yet. The instant is absolute rather than a
    /// countdown, because it is the deployment's clock rather than this terminal's that decides when the pass runs.
    /// <para>
    /// The absence names both of its causes and asserts neither, because a deployment that has only just started shows
    /// it as truthfully as one whose walk is turned off, and a line claiming the second would send an operator to a
    /// setting that is already what they want it to be.
    /// </para>
    /// </remarks>
    private static string DescribeNextPass(DateTimeOffset? dueAt) => dueAt is { } moment
        ? $"due at {moment:u}"
        : "none scheduled — either the deployment has only just started, or 'EmbeddingBackfill:Enabled' is false and it walks no stored mail.";

    /// <summary>States what the last call to the provider established, and when.</summary>
    /// <remarks>
    /// The moment is reported beside the state because the state is observed rather than probed: nothing calls a
    /// provider to answer this, so a failure recorded hours ago and one recorded a moment ago read alike without it.
    /// </remarks>
    private static string DescribeProvider(EmbeddingProviderHealth? provider)
    {
        if (provider is not { } observed)
        {
            return "not reported";
        }

        return observed.ObservedAt is { } moment
            ? $"{observed.State}, as of {moment:u}"
            : $"{observed.State}. No call has been made yet, so nothing is known.";
    }
}
