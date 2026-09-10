// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>Opens the deployment's persisted configuration document in the operator's editor, and commits what they saved.</summary>
/// <remarks>
/// <para>
/// The command for a change that is several settings at once. <c>set</c> and <c>unset</c> each name one path, so a
/// change spanning half a section is a run of commands each committing a version of its own, with every intermediate
/// one a configuration the deployment briefly ran on. This is the same change as one transaction: the document is
/// fetched with its version, edited, and committed against that version, so it is accepted whole or refused whole.
/// </para>
/// <para>
/// Three things the buffer is not. It is not the deployment's whole configuration — the layer is sparse, and what is
/// not in it is inherited from the files beneath. It does not carry secret material: every secret-bearing value reads
/// as the redaction marker, and a marker saved back leaves the setting exactly as it was. And it is not a file the
/// deployment reads — nothing here edits a configuration file, and the document is committed through the same writer
/// every other change goes through.
/// </para>
/// <para>
/// An emptied buffer aborts, which is the convention every editor-driven command an operator has met follows. A buffer
/// saved unchanged writes nothing either, and both are reported as what they are rather than as a failure.
/// </para>
/// </remarks>
internal static class EditConfigurationCommand
{
    /// <summary>Builds the <c>config edit</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var shadowedOption = ConfigurationOptions.EvenIfShadowed();

        Command command = new("edit", "Edit this deployment's persisted configuration document in your editor, and commit it as one change.")
        {
            shadowedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(shadowedOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        bool evenIfShadowed,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var editor = EditorDrivenDocument.EditorNamedByTheShell(context);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var client = new AdminApiClient(transport, context.Console);

        var opened = await client.ReadConfigurationDocumentAsync(profile.Token, cancellationToken);
        var document = opened.Document ?? string.Empty;

        var saved = await EditorDrivenDocument.OpenAsync(
            context,
            editor,
            "configuration",
            "the deployment's configuration",
            document,
            cancellationToken);

        return saved is null
            ? CliExitCode.Success
            : await CommitAsync(context, client, profile.Token, opened, saved, evenIfShadowed, cancellationToken);
    }

    private static async Task<int> CommitAsync(
        CliContext context,
        AdminApiClient client,
        string token,
        ConfigurationDocument opened,
        string saved,
        bool evenIfShadowed,
        CancellationToken cancellationToken)
    {
        var answer = await client.SaveConfigurationDocumentAsync(
            token,
            new ConfigurationDocumentRequest(opened.Version, saved, evenIfShadowed),
            cancellationToken);

        if (answer.Code == ConfigurationWriteAnswer.VersionSuperseded)
        {
            await ReportWhatMovedAsync(context, client, token, opened, answer, cancellationToken);

            return CliExitCode.Failure;
        }

        return ConfigurationOutput.ReportWrite(context, answer);
    }

    /// <summary>Says what the writer that committed first changed, so the operator can decide again against it.</summary>
    /// <remarks>
    /// The document now in force is fetched rather than described from the refusal, because what an operator has to see
    /// is what somebody else did rather than the fact that they did something. Nothing of this session is applied on
    /// top of it: merging two edits neither author saw is the one outcome the version guard exists to prevent.
    /// </remarks>
    private static async Task ReportWhatMovedAsync(
        CliContext context,
        AdminApiClient client,
        string token,
        ConfigurationDocument opened,
        ConfigurationWriteAnswer answer,
        CancellationToken cancellationToken)
    {
        foreach (var message in answer.DescribeRefusal())
        {
            context.Console.WriteError(message);
        }

        var inForce = await client.ReadConfigurationDocumentAsync(token, cancellationToken);
        var moved = SettingsBuffer.MovedBetween(opened.Document ?? string.Empty, inForce.Document ?? string.Empty);

        if (moved.Count == 0)
        {
            context.Console.WriteNotice(
                $"Version {inForce.Version.ToString(CultureInfo.InvariantCulture)} carries the same settings the buffer was opened over, so what moved was a value this reading redacts. Edit again to compose over it.");

            return;
        }

        context.Console.WriteNotice(
            $"These settings differ between version {opened.Version.ToString(CultureInfo.InvariantCulture)} and version {inForce.Version.ToString(CultureInfo.InvariantCulture)}:");

        foreach (var path in moved)
        {
            context.Console.WriteNotice($"  {path}");
        }
    }
}
