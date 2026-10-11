// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Users;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Reads one user's record as the deployment holds it.</summary>
/// <remarks>
/// What an administrator answers "which mailboxes does this deployment read for this person, and where is that decided"
/// from. The document arrives with every secret-bearing value replaced by the deployment's redaction marker, which is
/// what an editing session saves back unchanged to leave the reference beneath it alone. Redaction is about secrets and
/// nothing else: the mail server, the mailbox user name, the secret's declared name, and the folder aliases are printed
/// as the record holds them, and a mailbox user name is ordinarily the person's own address, so this output is one
/// person's mailbox identity rather than something to paste where the record itself would not go.
/// <para>
/// The document is the record's own. With <c>--effective</c> the command prints instead what the user is served with:
/// each property as the settings policies governing them resolve it, which step of the resolution order supplied it,
/// whose policy holds it against the person, and whether an administrator may change it.
/// </para>
/// </remarks>
internal static class ShowUserRecordCommand
{
    /// <summary>Builds the <c>user show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var formatOption = CliOptions.DocumentFormat();
        var userOption = UserOptions.User();
        Option<bool> effectiveOption = new("--effective")
        {
            Description = "Print each setting as the user is served it, with the settings policy or record that supplied it, instead of the record's own document.",
        };

        Command command = new("show", "Read one user's record as this deployment holds it.")
        {
            userOption,
            formatOption,
            effectiveOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(formatOption),
            result.GetValue(effectiveOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        DocumentView view,
        bool effective,
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

        var record = await deployment.ReadUserRecordAsync(profile.Token, user, cancellationToken);

        context.Console.WriteLine($"{record.DisplayName} ({record.User:D})");
        context.Console.WriteLine($"  version: {record.Version.ToString(CultureInfo.InvariantCulture)}");

        if (!effective)
        {
            context.Console.WriteLine(EditorDrivenDocument.Show(record.Document ?? "{}", view));

            return CliExitCode.Success;
        }

        if (record.Unresolved is { } unresolved)
        {
            context.Console.WriteLine($"  {ConsoleSafeText.Sanitize(unresolved)}");

            return CliExitCode.Success;
        }

        foreach (var property in record.Properties ?? [])
        {
            context.Console.WriteLine(Describe(property));
        }

        return CliExitCode.Success;
    }

    /// <summary>Writes one resolved setting as a line: what it is, where it came from, and who holds it.</summary>
    /// <remarks>
    /// Every part the deployment answered with is reduced before it is written. A value is what somebody stated — the
    /// name is one a person sets for themselves — so it could otherwise carry the characters a terminal acts on into
    /// an administrator's listing.
    /// </remarks>
    private static string Describe(UserRecordProperty property)
    {
        var value = property.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } stated
            ? ConsoleSafeText.Sanitize(stated.ValueKind == JsonValueKind.String ? stated.GetString() : stated.GetRawText())
            : "(nothing stated)";
        var heldBy = property.HeldBy is { } holder
            ? $", held by the {ConsoleSafeText.Sanitize(holder.ToLowerInvariant())}"
            : string.Empty;
        var changeable = property switch
        {
            { Changeable: true } => string.Empty,
            { Source: { } source } when source.EndsWith("Forced", StringComparison.Ordinal) => ", not changeable while the policy forces it",
            _ => ", not changeable under your grant",
        };

        return $"  {ConsoleSafeText.Sanitize(property.Path)}: {value}  [{ConsoleSafeText.Sanitize(property.Source)}{heldBy}{changeable}]";
    }
}
