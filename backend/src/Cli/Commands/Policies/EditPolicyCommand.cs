// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Configuration;
using MailFathom.Cli.Administration.Policies;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Policies;

/// <summary>Opens one scope's settings policy in the operator's editor, and commits what they saved.</summary>
/// <remarks>
/// <para>
/// The only command that changes a policy, because a policy is read and written whole: it is fetched with its version,
/// edited, and committed against that version, so it is accepted whole or refused whole. Without <c>--organization</c>
/// the policy is the deployment's own, and with it that organization's and no other.
/// </para>
/// <para>
/// What reaches the buffer is the policy exactly as it is stored, because a policy holds no secret and the deployment
/// redacts nothing in it. The buffer still lives in a directory readable by its user alone and is discarded whichever
/// way the session ends, as every editing session's is.
/// </para>
/// </remarks>
internal static class EditPolicyCommand
{
    /// <summary>Builds the <c>policy edit</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var formatOption = CliOptions.DocumentFormat();
        var organizationOption = PolicyOptions.Organization();

        Command command = new("edit", "Edit this deployment's settings policy, or an organization's, in your editor, and commit it as one change.")
        {
            organizationOption,
            formatOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(organizationOption),
            result.GetValue(formatOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Reads the policy, offers it to the editor, and commits what came back.</summary>
    /// <exception cref="CliFailure">Thrown when no editor is named, or the session did not finish.</exception>
    /// <remarks>
    /// The editor is looked for before the deployment is reached, as <c>config edit</c> does it, so an operator whose
    /// shell names none is told so without a request going out.
    /// </remarks>
    private static async Task<int> RunAsync(
        CliContext context,
        Guid? organizationId,
        DocumentView view,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var editor = EditorDrivenDocument.EditorNamedByTheShell(context);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var opened = await deployment.ReadSettingsPolicyAsync(profile.Token, organizationId, cancellationToken);

        var saved = await EditorDrivenDocument.OpenAsync(
            context,
            editor,
            "settings-policy",
            organizationId is null ? "the deployment's settings policy" : "this organization's settings policy",
            opened.Document ?? "{}",
            view,
            cancellationToken);

        return saved is null
            ? CliExitCode.Success
            : await CommitAsync(context, deployment, profile.Token, organizationId, opened, saved, cancellationToken);
    }

    private static async Task<int> CommitAsync(
        CliContext context,
        AdminApiClient deployment,
        string token,
        Guid? organizationId,
        SettingsPolicy opened,
        string saved,
        CancellationToken cancellationToken)
    {
        var answer = await deployment.SaveSettingsPolicyAsync(
            token,
            organizationId,
            new SettingsPolicySaveRequest(opened.Version, saved),
            cancellationToken);

        var outcome = PolicyOutput.ReportWrite(context, answer);

        // The deployment reports a policy composed over a superseded version with the same code a configuration write
        // is refused with, because it is the same guard over a different document.
        if (answer.Code == ConfigurationWriteAnswer.VersionSuperseded)
        {
            await ReportWhatMovedAsync(context, deployment, token, organizationId, opened, cancellationToken);
        }

        return outcome;
    }

    /// <summary>Says what the writer that committed first changed, so the operator can decide again against it.</summary>
    /// <remarks>
    /// The policy now in force is fetched rather than described from the refusal, because what an operator has to see is
    /// what somebody else did rather than the fact that they did something. Nothing of this session is applied on top of
    /// it: merging two edits neither author saw is the one outcome the version guard exists to prevent. A policy is not
    /// redacted, so two versions that read alike here are alike, and the one in force already states everything this
    /// session started from.
    /// </remarks>
    private static async Task ReportWhatMovedAsync(
        CliContext context,
        AdminApiClient deployment,
        string token,
        Guid? organizationId,
        SettingsPolicy opened,
        CancellationToken cancellationToken)
    {
        var inForce = await deployment.ReadSettingsPolicyAsync(token, organizationId, cancellationToken);
        var moved = SettingsBuffer.MovedBetween(opened.Document ?? "{}", inForce.Document ?? "{}");

        if (moved.Count == 0)
        {
            context.Console.WriteNotice(
                $"Version {inForce.Version.ToString(CultureInfo.InvariantCulture)} carries the same settings the buffer was opened over, so the policy in force already states what this session started from. Edit again to compose over it.");

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
