// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Reports one setting as the deployment reads it, with the layer that decided it.</summary>
/// <remarks>
/// The source is the answer as much as the value is. A deployment composes its settings from files, a persisted layer,
/// and the three sources an operator reaches for when something is wrong, so "what does this setting say" and "where
/// would I change it" are one question — and answering the first alone is what leads to a persisted write that commits
/// and changes nothing.
/// </remarks>
internal static class GetSettingCommand
{
    /// <summary>Builds the <c>config get</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Argument<string> pathArgument = new("path")
        {
            Description = "The colon-delimited configuration path to read, such as MailboxSearch:SnippetsPerEmail.",
        };

        Command command = new("get", "Report one setting as this deployment reads it, and which layer decided it.")
        {
            pathArgument,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(pathArgument) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string path,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var reading = await new AdminApiClient(transport, context.Console)
            .ReadConfigurationAsync(profile.Token, path, cancellationToken);

        // The path itself rather than the first entry the prefix matched, because a path is a prefix of the settings
        // beneath it: asking for MailboxSearch and being handed MailboxSearch:SnippetsPerEmail would answer a question
        // nobody asked. Compared without regard to case for the reason every configuration reader does.
        var setting = reading.Settings?.FirstOrDefault(
            candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));

        ConfigurationOutput.WriteSetting(context, path, setting);

        return CliExitCode.Success;
    }
}
