// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Takes the settings a deployment's files decide beneath one path into its persisted configuration.</summary>
/// <remarks>
/// <para>
/// The one command that moves a decision from a file into the database, and the only thing in MailFathom that ever
/// does. No upgrade, no import, and no first start copies a file-supplied value into the persisted layer, so a
/// deployment that never runs this keeps its files as the whole truth about its own configuration — which is what makes
/// a committed ConfigMap reviewable as the thing actually in force.
/// </para>
/// <para>
/// It is previewed and then confirmed, because of what it costs afterwards: the settings it copies stop being decided
/// by the files, and editing the file they came from no longer changes what the deployment does. The preview names
/// every setting and the file behind it, which is the moment to notice that a path covers more than was meant.
/// </para>
/// <para>
/// A setting the persisted layer already carries is not offered. Adopting it would replace a value somebody persisted
/// deliberately with the file's, which is the opposite of taking a decision into the database; changing a persisted
/// value is what <c>config set</c> is for.
/// </para>
/// </remarks>
internal static class AdoptSettingsCommand
{
    /// <summary>Builds the <c>config adopt</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var shadowedOption = ConfigurationOptions.EvenIfShadowed();
        var confirmationOption = CliOptions.Confirmed("adoption");

        Argument<string> prefixArgument = new("prefix")
        {
            Description = "The colon-delimited path to adopt beneath, such as MailboxSearch. There is no adoption of the whole configuration.",
        };

        Command command = new("adopt", "Copy what this deployment's files decide beneath a path into its persisted configuration.")
        {
            prefixArgument,
            confirmationOption,
            shadowedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(prefixArgument) ?? string.Empty,
            result.GetValue(confirmationOption),
            result.GetValue(shadowedOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string prefix,
        bool confirmedUpFront,
        bool evenIfShadowed,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var client = new AdminApiClient(transport, context.Console);

        var preview = await client.ReadAdoptableConfigurationAsync(profile.Token, prefix, cancellationToken);

        if (preview.Settings is not { Count: > 0 } adoptable)
        {
            context.Console.WriteLine(
                $"This deployment's files supply nothing beneath {prefix} that its persisted configuration does not already carry, so there is nothing to adopt.");

            return CliExitCode.Success;
        }

        WritePreview(context, prefix, adoptable);

        if (!Agreed(context, prefix, adoptable.Count, confirmedUpFront))
        {
            context.Console.WriteLine("Nothing was adopted.");

            return CliExitCode.Success;
        }

        var answer = await client.AdoptConfigurationAsync(
            profile.Token,
            new ConfigurationAdoptionRequest(preview.Version, prefix, evenIfShadowed),
            cancellationToken);

        return ConfigurationOutput.ReportWrite(context, answer);
    }

    private static void WritePreview(
        CliContext context,
        string prefix,
        IReadOnlyList<EffectiveSettingRecord> adoptable)
    {
        context.Console.WriteLine(
            $"Adopting {prefix} would persist {adoptable.Count.ToString(CultureInfo.InvariantCulture)} settings the deployment's files decide today:");

        ConfigurationOutput.WriteTree(context, adoptable);

        context.Console.WriteNotice(
            "Once adopted, these settings are decided by the persisted document. Editing the file each one came from will no longer change what this deployment does; 'mfctl config unset' is what gives a setting back to its file.");
    }

    private static bool Agreed(CliContext context, string prefix, int count, bool confirmedUpFront) =>
        CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            $"Adopting {prefix} would persist {count.ToString(CultureInfo.InvariantCulture)} settings the deployment's files decide, and there is nobody at the terminal to confirm it. Re-run with --yes to state the agreement in the command.",
            $"Persist these {count.ToString(CultureInfo.InvariantCulture)} settings, so the files stop deciding them? [y/N] ");
}
