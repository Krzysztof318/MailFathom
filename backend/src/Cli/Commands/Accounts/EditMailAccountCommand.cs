// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Accounts;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Opens one mail account's declaration in the operator's editor, and commits what they saved.</summary>
/// <remarks>
/// What reaches the buffer is the declaration as the deployment redacted it, so a secret-bearing value reads as the
/// redaction marker and a marker saved back leaves the reference beneath it alone. The address and the mail server do
/// reach it, which is why the buffer lives in a directory readable by its user alone and is discarded whichever way the
/// session ends. The save is committed against the version the buffer was opened over, so it is accepted whole or
/// refused whole.
/// </remarks>
internal static class EditMailAccountCommand
{
    /// <summary>Builds the <c>account edit</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var formatOption = CliOptions.DocumentFormat();
        var accountOption = MailAccountOptions.Account();

        Command command = new("edit", "Edit one mail account's declaration in your editor, and commit it as one change.")
        {
            accountOption,
            formatOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            result.GetValue(formatOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Reads the declaration, offers it to the editor, and commits what came back.</summary>
    /// <remarks>The editor is looked for before the deployment is reached, so an operator whose shell names none is told so without a request going out.</remarks>
    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        DocumentView view,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var editor = EditorDrivenDocument.EditorNamedByTheShell(context);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var opened = await deployment.ReadMailAccountAsync(profile.Token, accountId, cancellationToken);

        var saved = await EditorDrivenDocument.OpenAsync(
            context,
            editor,
            "mail-account",
            "this mail account's declaration",
            opened.Declaration ?? string.Empty,
            view,
            cancellationToken);

        if (saved is null)
        {
            return CliExitCode.Success;
        }

        var answer = await deployment.SaveMailAccountAsync(
            profile.Token,
            accountId,
            new MailAccountSaveRequest(opened.Version, saved),
            cancellationToken);

        return MailAccountOutput.ReportWrite(context, answer);
    }
}
