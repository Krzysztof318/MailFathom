// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;
using MailFathom.Cli.Credentials;

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
        var editor = EditorNamedByTheShell(context);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var client = new AdminApiClient(transport, context.Console);

        var opened = await client.ReadConfigurationDocumentAsync(profile.Token, cancellationToken);
        var document = opened.Document ?? string.Empty;
        var session = Path.Combine(Path.GetTempPath(), $"mailfathom-configuration-{Guid.NewGuid():N}");
        var buffer = Path.Combine(session, "configuration.json");

        try
        {
            Open(session, buffer, document);

            if (context.Edit(editor, buffer) is { Saved: false } ended)
            {
                throw new CliFailure(WhyNothingWasWritten(editor, ended));
            }

            var saved = await ReadBackAsync(buffer, cancellationToken);

            if (Abandoned(context, document, saved))
            {
                return CliExitCode.Success;
            }

            return await CommitAsync(context, client, profile.Token, opened, saved, evenIfShadowed, cancellationToken);
        }
        finally
        {
            Discard(session);
        }
    }

    /// <summary>Opens the session's own directory and writes the document into it, readable by their owner alone.</summary>
    /// <exception cref="CliFailure">Thrown when the temporary directory cannot be written, which is a situation rather than a defect.</exception>
    /// <remarks>
    /// A temporary directory that is full, read-only, or on a filesystem that will not take an owner-only mode is
    /// something the operator can act on, so it is reported as a sentence naming the path rather than left to reach
    /// <c>CliRunner</c> as a stack trace — the same answer the credential store and the token protector give for the
    /// same situation.
    /// </remarks>
    private static void Open(string session, string buffer, string document)
    {
        try
        {
            OwnerOnlyStorage.CreateDirectory(session);

            SettingsBuffer.Write(buffer, document);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The editing buffer at {buffer} could not be written.", failure);
        }
    }

    /// <summary>Reads back what the editor saved.</summary>
    /// <exception cref="CliFailure">Thrown when the buffer cannot be read, which the editor rather than this command left it as.</exception>
    private static async Task<string> ReadBackAsync(string buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(buffer, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure(
                $"The editing buffer at {buffer} could not be read back after the editor exited, so nothing was written.",
                failure);
        }
    }

    /// <summary>Removes the session's directory and the buffer in it, letting the invocation's own outcome stand where it cannot be removed.</summary>
    /// <remarks>
    /// <para>
    /// The case is the one this command's own guidance anticipates: a graphical editor started without its wait flag
    /// returns while still holding the file open, so the delete throws on Windows over an invocation that had already
    /// decided what it did. Turning that into a stack trace would report a failure to somebody whose command worked,
    /// or replace the sentence naming why nothing was written — which is the rule <c>CliRunner.Record</c> states for a
    /// full disk.
    /// </para>
    /// <para>
    /// What is left behind on that path is the session's own directory, which is readable by its owner alone whatever
    /// the editor did to the file inside it. That is why the buffer sits in a directory of its own rather than in the
    /// temporary directory itself: an editor that saves by writing a sibling and renaming it over the target creates
    /// that sibling under the process umask, so the mode this command set at creation does not survive the first save
    /// — and what the file then holds is the deployment's whole persisted configuration plus anything the operator
    /// typed over a marker.
    /// </para>
    /// </remarks>
    private static void Discard(string session)
    {
        try
        {
            Directory.Delete(session, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reports whether the editing session asked for nothing, and says which of the two ways it did.</summary>
    /// <remarks>
    /// An emptied buffer is the conventional way to abandon an editor-driven command and is honoured as one, rather
    /// than being read as a document that persists no settings at all — which is a change an operator can still make
    /// deliberately by saving an empty JSON object.
    /// </remarks>
    private static bool Abandoned(CliContext context, string document, string saved)
    {
        if (string.IsNullOrWhiteSpace(saved))
        {
            context.Console.WriteLine("The buffer was emptied, so the deployment's configuration was left as it was.");

            return true;
        }

        if (string.Equals(saved, document, StringComparison.Ordinal))
        {
            context.Console.WriteLine("The buffer was saved unchanged, so nothing was written.");

            return true;
        }

        return false;
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

    /// <summary>Says why an editing session that did not finish wrote nothing, in terms the operator can act on.</summary>
    /// <remarks>
    /// The two endings need different advice. A wait flag is what repairs an editor that ran and returned before the
    /// operator had finished; it repairs nothing for an editor the operating system never started, where the value in
    /// the variable is the thing to correct and the system's own words are what name which way it is wrong.
    /// </remarks>
    private static string WhyNothingWasWritten(string editor, EditingSession ended) =>
        ended.WhyItNeverStarted is { Length: > 0 } reason
            ? $"The editor '{editor}' could not be started, so nothing was written: {reason}. Correct ${OperatorEditor.VisualVariable} or ${OperatorEditor.EditorVariable} to name a program on this machine."
            : $"The editor '{editor}' did not finish successfully, so nothing was written. A graphical editor needs the flag that makes it wait — '{OperatorEditor.VisualVariable}=\"code --wait\"', for instance — because this command reads the file back when the editor exits.";

    /// <summary>Finds the editor the operator's shell names, refusing rather than choosing one for them.</summary>
    /// <exception cref="CliFailure">Thrown when neither variable names an editor.</exception>
    private static string EditorNamedByTheShell(CliContext context) =>
        context.Variable(OperatorEditor.VisualVariable) is { Length: > 0 } visual ? visual
        : context.Variable(OperatorEditor.EditorVariable) is { Length: > 0 } editor ? editor
        : throw new CliFailure(
            $"No editor is named for this shell, so there is nothing to open the document in. Set ${OperatorEditor.VisualVariable} or ${OperatorEditor.EditorVariable} — a graphical editor needs the flag that makes it wait, such as 'code --wait'.");
}
