// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Stops the persisted document carrying one setting, so the source beneath it decides again.</summary>
/// <remarks>
/// A command of its own rather than a set with an empty value, because the two are opposite acts. The persisted layer
/// is sparse: a setting the document does not carry is inherited from the file beneath it, and a setting carrying an
/// empty value shadows that file with nothing. Only the first is what an operator undoing a persisted setting means,
/// and a flag deciding which would make a typo the difference between restoring a deployment's configured value and
/// blanking it.
/// </remarks>
internal static class UnsetSettingCommand
{
    /// <summary>Builds the <c>config unset</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var shadowedOption = ConfigurationOptions.EvenIfShadowed();

        Argument<string> pathArgument = new("path")
        {
            Description = "The colon-delimited configuration path to stop persisting, such as MailboxSearch:SnippetsPerEmail.",
        };

        Command command = new("unset", "Stop persisting one setting, so the deployment's own files decide it again.")
        {
            pathArgument,
            shadowedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(pathArgument) ?? string.Empty,
            result.GetValue(shadowedOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string path,
        bool evenIfShadowed,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var client = new AdminApiClient(transport, context.Console);

        var reading = await client.ReadConfigurationAsync(profile.Token, path, cancellationToken);

        var answer = await client.WriteConfigurationAsync(
            profile.Token,
            new ConfigurationWriteRequest(reading.Version, [new ConfigurationChangeRequest(path, Value: null)], evenIfShadowed),
            cancellationToken);

        return ConfigurationOutput.ReportWrite(context, answer);
    }
}
