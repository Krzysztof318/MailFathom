// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Persists one setting, having composed the change over the version the deployment is running on.</summary>
/// <remarks>
/// <para>
/// The write is read first and not because the value is needed: the reading carries the persisted version, and a
/// change composed over a version fetched apart from the settings it describes is the lost update the deployment's
/// version guard exists to refuse. Two administrators editing at once is what decides that, and the second of them is
/// told to read and decide again rather than having their change merged with somebody's they never saw.
/// </para>
/// <para>
/// A setting a source above the persisted layer supplies is refused rather than committed silently, by the deployment
/// rather than by this command. What this adds is the sentence naming the flag: staging a value beneath an override
/// about to be removed is a thing an operator legitimately means, and it is the only case in which persisting a value
/// nothing will read is right.
/// </para>
/// </remarks>
internal static class SetSettingCommand
{
    /// <summary>Builds the <c>config set</c> command.</summary>
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
            Description = "The colon-delimited configuration path to persist, such as MailboxSearch:SnippetsPerEmail.",
        };

        Argument<string> valueArgument = new("value")
        {
            Description = "The value the setting takes, written as configuration writes one: a number, a word, or true or false, all as text.",
        };

        Command command = new("set", "Persist one setting in this deployment's own configuration document.")
        {
            pathArgument,
            valueArgument,
            shadowedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(pathArgument) ?? string.Empty,
            result.GetValue(valueArgument) ?? string.Empty,
            result.GetValue(shadowedOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string path,
        string value,
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
            new ConfigurationWriteRequest(reading.Version, [new ConfigurationChangeRequest(path, value)], evenIfShadowed),
            cancellationToken);

        return ConfigurationOutput.ReportWrite(context, answer);
    }
}
