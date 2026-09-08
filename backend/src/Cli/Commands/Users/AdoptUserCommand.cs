// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Moves one user's mail accounts out of this deployment's files and into their own record.</summary>
/// <remarks>
/// <para>
/// The one act that hands a user over from configuration to their own record, and the only thing in MailFathom that
/// ever does it. No upgrade, no first start, and no ordinary edit sets the marker behind an operator's back, so a
/// deployment that never runs this keeps its files as the whole truth about whose mailboxes it reads — which is what
/// makes a committed configuration reviewable as the thing actually in force.
/// </para>
/// <para>
/// It is previewed and then confirmed, because of what it costs afterwards: the mailboxes it copies stop being decided
/// by the files, and editing the section they came from no longer changes what this deployment reads for that person.
/// The preview names the path behind them, which is the moment to notice that it covers more than was meant.
/// </para>
/// <para>
/// Every other user goes on being read from the files. The handover is per user and this is where each one happens.
/// </para>
/// </remarks>
internal static class AdoptUserCommand
{
    /// <summary>Builds the <c>user adopt</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();
        var confirmationOption = CliOptions.Confirmed("adoption");

        Command command = new(
            "adopt",
            "Move one user's mail accounts out of this deployment's files and into their own record.")
        {
            userOption,
            confirmationOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(confirmationOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        bool confirmedUpFront,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(
            deployment,
            profile.Token,
            requestedUser,
            cancellationToken);

        var preview = await deployment.ReadUserAdoptionAsync(profile.Token, user, cancellationToken);

        if (!preview.ReadFromConfiguration)
        {
            context.Console.WriteLine(
                $"{Named(preview)} already reads their mail accounts from their own record, so there is nothing to adopt.");

            return CliExitCode.Success;
        }

        WritePreview(context, preview);

        if (!Agreed(context, preview, confirmedUpFront))
        {
            // Reported on standard error and with a failing code, which is what every other command does when it did
            // not do what it was asked.
            context.Console.WriteError("Nothing was adopted.");

            return CliExitCode.Failure;
        }

        var answer = await deployment.AdoptUserAsync(
            profile.Token,
            user,
            new UserAdoptionRequest(preview.Version),
            cancellationToken);

        return UserOutput.ReportWrite(context, answer);
    }

    private static void WritePreview(CliContext context, UserAdoptionPreview preview)
    {
        var accounts = preview.MailAccounts ?? [];

        context.Console.WriteLine(
            $"Adopting {Named(preview)} would move {accounts.Count.ToString(CultureInfo.InvariantCulture)} mail accounts into their own record:");

        foreach (var account in accounts)
        {
            context.Console.WriteLine($"  {account.AccountId} ({account.DisplayName})");
        }

        if (preview.ConfigurationPath is { Length: > 0 } path)
        {
            context.Console.WriteLine($"  from {path}");
        }

        WriteClassification(context, preview);
        WriteSensitiveContent(context, preview);

        context.Console.WriteNotice(
            "Once adopted, these mail accounts are decided by this user's record. Editing the configuration they came "
            + "from will no longer change what the deployment reads for them; 'mfctl user account' is what changes "
            + "them afterwards. Every other user goes on being read from the files. Remove the declarations this "
            + "adoption copied: a section no served user reads is still resolved before any record, and the next "
            + "start refuses rather than reading it.");
    }

    /// <summary>Names the classification posture the adoption would commit beside the mailboxes.</summary>
    /// <remarks>
    /// Written out setting by setting rather than summarized, because two of them file mail and mark it read on this
    /// user's own mail server, and the adoption cannot be undone. A deployment stating no posture prints nothing
    /// rather than an empty heading.
    /// </remarks>
    private static void WriteClassification(CliContext context, UserAdoptionPreview preview)
    {
        var posture = preview.Classification ?? [];

        if (posture.Count == 0)
        {
            return;
        }

        context.Console.WriteLine(
            "It would also commit this deployment's spam classification posture into their record, which decides what "
            + "happens to their junk from then on:");

        foreach (var setting in posture)
        {
            context.Console.WriteLine($"  {setting.Path} = {setting.Value}");
        }
    }

    /// <summary>Names the scanning block the adoption would commit beside the mailboxes.</summary>
    /// <remarks>
    /// Written out for the same reason the classification posture is: it decides whether this user's mail is scanned
    /// before it is stored, indexed, or published, and a handover that left it behind would switch a scanner off over
    /// their mail with nothing saying so. A declaration stating none prints nothing rather than an empty heading.
    /// </remarks>
    private static void WriteSensitiveContent(CliContext context, UserAdoptionPreview preview)
    {
        var scanning = preview.SensitiveContent ?? [];

        if (scanning.Count == 0)
        {
            return;
        }

        context.Console.WriteLine(
            "It would also commit the scanning posture their declaration states into their record, which decides what "
            + "their mail is scanned for from then on:");

        foreach (var setting in scanning)
        {
            context.Console.WriteLine($"  {setting.Path} = {setting.Value}");
        }
    }

    private static bool Agreed(CliContext context, UserAdoptionPreview preview, bool confirmedUpFront)
    {
        var count = (preview.MailAccounts?.Count ?? 0).ToString(CultureInfo.InvariantCulture);

        return CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            $"Adopting {Named(preview)} would move {count} mail accounts out of this deployment's configuration, and there is nobody at the terminal to confirm it. Re-run with --yes to state the agreement in the command.",
            $"Move these {count} mail accounts into this user's record, so the configuration stops deciding them? [y/N] ");
    }

    private static string Named(UserAdoptionPreview preview) =>
        string.IsNullOrWhiteSpace(preview.DisplayName)
            ? preview.User.ToString("D", null)
            : $"{preview.DisplayName} ({preview.User:D})";
}
