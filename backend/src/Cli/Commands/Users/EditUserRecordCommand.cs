// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;
using MailFathom.Cli.Administration.Users;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Opens one user's record in the operator's editor, and commits what they saved.</summary>
/// <remarks>
/// <para>
/// The command for a change to a record that is more than one mailbox. <c>account add</c> and <c>account remove</c>
/// each name one mailbox and the deployment composes it into the document, so changing two of them, or anything else a
/// record carries, is a run of commands each committing a version of its own — with every intermediate one an account
/// set the deployment briefly read. This is the same change as one transaction: the record is fetched with its version,
/// edited, and committed against that version, so it is accepted whole or refused whole.
/// </para>
/// <para>
/// What reaches the buffer is the record as the deployment redacted it, so a secret-bearing value reads as the
/// redaction marker and a marker saved back leaves the reference beneath it alone. What does reach it is one person's
/// mailbox identity — their mail server and their mailbox user name, which is ordinarily their own address — which is
/// why the buffer lives in a directory readable by its user alone and is discarded whichever way the session ends.
/// </para>
/// </remarks>
internal static class EditUserRecordCommand
{
    /// <summary>Builds the <c>user edit</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Command command = new("edit", "Edit one user's record in your editor, and commit it as one change.")
        {
            userOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Reads the record, offers it to the editor, and commits what came back.</summary>
    /// <exception cref="CliFailure">Thrown when no editor is named, the session did not finish, or a configuration source still supplies this user's mail accounts.</exception>
    /// <remarks>
    /// The editor is looked for before the deployment is reached, as <c>config edit</c> does it, so an operator whose
    /// shell names none is told so without a request going out.
    /// </remarks>
    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var editor = EditorDrivenDocument.EditorNamedByTheShell(context);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(
            deployment,
            profile.Token,
            requestedUser,
            cancellationToken);

        var opened = await deployment.ReadUserRecordAsync(profile.Token, user, cancellationToken);

        // Refused before the editor opens rather than after the commit, although the deployment refuses it either way:
        // the record already says a configuration source supplies this user, and opening an empty buffer over that
        // would spend the operator's editing session on a write that was never going to be accepted.
        if (opened.ReadFromConfiguration)
        {
            throw new CliFailure(UserOutput.RecordSuppliedByAConfigurationSource);
        }

        var saved = await EditorDrivenDocument.OpenAsync(
            context,
            editor,
            "user-record",
            "this user's record",
            opened.Document ?? string.Empty,
            cancellationToken);

        return saved is null
            ? CliExitCode.Success
            : await CommitAsync(context, deployment, profile.Token, user, opened, saved, cancellationToken);
    }

    private static async Task<int> CommitAsync(
        CliContext context,
        AdminApiClient deployment,
        string token,
        Guid user,
        UserRecord opened,
        string saved,
        CancellationToken cancellationToken)
    {
        var answer = await deployment.SaveUserRecordAsync(
            token,
            user,
            new UserRecordSaveRequest(opened.Version, saved),
            cancellationToken);

        var outcome = UserOutput.ReportWrite(context, answer);

        // The deployment reports a record composed over a superseded version with the same code a configuration write
        // is refused with, because it is the same guard over a different document.
        if (answer.Code == ConfigurationWriteAnswer.VersionSuperseded)
        {
            await ReportWhatMovedAsync(context, deployment, token, user, opened, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Says what the writer that committed first changed, so the operator can decide again against it.</summary>
    /// <remarks>
    /// The record now in force is fetched rather than described from the refusal, because what an operator has to see is
    /// what somebody else did rather than the fact that they did something. Nothing of this session is applied on top of
    /// it: merging two edits neither author saw is the one outcome the version guard exists to prevent. The other writer
    /// is routinely the person themselves, from the client, which is why this names the mailboxes rather than only the
    /// version.
    /// </remarks>
    private static async Task ReportWhatMovedAsync(
        CliContext context,
        AdminApiClient deployment,
        string token,
        Guid user,
        UserRecord opened,
        CancellationToken cancellationToken)
    {
        var inForce = await deployment.ReadUserRecordAsync(token, user, cancellationToken);
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
