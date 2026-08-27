// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Reports a whole section of the deployment's settings as a tree, with the layer that decided each value.</summary>
/// <remarks>
/// The reading a person starts from. <c>get</c> answers a question about a setting somebody already knows the name of;
/// this is how they find out which settings exist at all, what the deployment made of them, and which of them a file,
/// the database, or an override beside the process is deciding.
/// </remarks>
internal static class ShowSettingsCommand
{
    /// <summary>Builds the <c>config show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Argument<string?> prefixArgument = new("prefix")
        {
            Description = "The colon-delimited path to read beneath, such as MailboxSearch. Omit it for every setting the deployment composed.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        Command command = new("show", "Report a section of this deployment's settings as a tree, with the layer that decided each value.")
        {
            prefixArgument,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(prefixArgument),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? prefix,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var reading = await new AdminApiClient(transport, context.Console)
            .ReadConfigurationAsync(profile.Token, prefix, cancellationToken);

        if (reading.Settings is not { Count: > 0 } settings)
        {
            context.Console.WriteLine(prefix is { Length: > 0 } named
                ? $"No source supplies any setting beneath {named}."
                : "This deployment composed no settings, which is a state nothing but an empty configuration produces.");

            return CliExitCode.Success;
        }

        ConfigurationOutput.WriteTree(context, settings);

        context.Console.WriteLine(
            $"{settings.Count.ToString(CultureInfo.InvariantCulture)} settings, over persisted configuration version {reading.Version.ToString(CultureInfo.InvariantCulture)}.");

        return CliExitCode.Success;
    }
}
