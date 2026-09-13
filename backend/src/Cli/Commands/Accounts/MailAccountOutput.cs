// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Cli.Administration.Accounts;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>How the account commands print an account and report what a write to one did.</summary>
/// <remarks>
/// An account's address and display name are printed because they are what an operator tells accounts apart by, and an
/// address is one person's mailbox identity: this output belongs where the account itself would go. Nothing here prints
/// a credential, which the deployment already redacted.
/// </remarks>
internal static class MailAccountOutput
{
    /// <summary>Writes one account as a heading line and the users it is assigned to.</summary>
    /// <param name="console">Where the account is written.</param>
    /// <param name="account">The account.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteHeading(ICliConsole console, MailAccountEntry account)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(account);

        var (emailAddress, displayName) = NamesIn(account.Declaration);
        var users = account.Users is { Count: > 0 } assigned
            ? string.Join(", ", assigned.Select(user => user.ToString("D")))
            : "nobody";

        console.WriteLine($"{account.Id:D}  {displayName ?? "(no display name)"} <{emailAddress ?? "no address"}>");
        console.WriteLine($"    version {account.Version.ToString(CultureInfo.InvariantCulture)}; assigned to: {users}");

        if (emailAddress is null)
        {
            console.WriteLine("    not served until it states an EmailAddress; set one with 'mfctl account edit'");
        }
    }

    /// <summary>Reports what one write to an account did, and returns what the command exits with.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="answer">What the deployment said.</param>
    /// <returns>Success where the write committed or there was nothing to change, and failure where the deployment refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static int ReportWrite(CliContext context, MailAccountWriteAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(answer);

        if (answer.Committed)
        {
            context.Console.WriteLine(answer.AccountId is { } created
                ? $"Created mail account {created:D}."
                : $"Committed mail account version {answer.Version.ToString(CultureInfo.InvariantCulture)}.");
            context.Console.WriteNotice(
                "Every replica serves the change once it reads it back. A synchronization run already in flight "
                + "finishes against the previous settings; the next run uses these.");

            foreach (var standingProblem in answer.Messages ?? [])
            {
                context.Console.WriteNotice(standingProblem);
            }

            return CliExitCode.Success;
        }

        var refused = answer.Code is not null;

        foreach (var message in answer.DescribeRefusal())
        {
            if (refused)
            {
                context.Console.WriteError(message);
            }
            else
            {
                context.Console.WriteLine(message);
            }
        }

        return refused ? CliExitCode.Failure : CliExitCode.Success;
    }

    private static (string? EmailAddress, string? DisplayName) NamesIn(string? declaration)
    {
        try
        {
            return JsonNode.Parse(declaration ?? "{}") is JsonObject parsed
                ? (TextOf(parsed, "EmailAddress"), TextOf(parsed, "DisplayName"))
                : (null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? TextOf(JsonObject declaration, string property) =>
        declaration[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
