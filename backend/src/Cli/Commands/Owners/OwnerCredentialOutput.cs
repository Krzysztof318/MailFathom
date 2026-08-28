// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration.Owners;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Writes what the credential commands print, so every one of them prints a credential the same way.</summary>
/// <remarks>
/// Nothing here prints a password, and nothing here has one to print: the deployment publishes a username, a state, and
/// two instants, and that is the whole of what these commands ever hold. A password typed at the prompt reaches the
/// request and nothing else — not the output, not a confirmation, and not a refusal.
/// </remarks>
internal static class OwnerCredentialOutput
{
    /// <summary>Prints one owner's credentials as a listing.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="credentials">The credentials to print, in the order the deployment served them.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteListing(ICliConsole console, IReadOnlyList<OwnerCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(credentials);

        CliTable listing = new("Credential", "Username", "State", "Provisioned", "Password changed");

        foreach (var credential in credentials)
        {
            listing.AddRow(
                $"{credential.Id:D}",
                credential.Username ?? "unreported",
                credential.Enabled ? "enabled" : "disabled",
                $"{credential.CreatedAt:u}",
                $"{credential.PasswordChangedAt:u}");
        }

        console.Write(listing);
    }

    /// <summary>Reads the password a command is about to send, refusing an empty one before a request is made.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="prompt">What to ask for, written only when a person is there to read it.</param>
    /// <returns>The password.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when nothing was supplied, which for a pipe is an exhausted input rather than a decision.</exception>
    /// <remarks>
    /// The only check performed here. Everything else about what a password may be is the deployment's policy, which it
    /// states in its own refusal — restating it in the command would leave two rules to keep in agreement, and the one
    /// that mattered would be the one the operator was not reading.
    /// </remarks>
    internal static string ReadPassword(CliContext context, string prompt)
    {
        ArgumentNullException.ThrowIfNull(context);

        var password = context.Console.ReadSecret(prompt);

        return password.Length > 0
            ? password
            : throw new CliFailure(
                "No password was supplied, so nothing was sent. Type one at the prompt, or pipe it in as a single line "
                + "when running without a terminal.");
    }
}
