// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration.Owners;
using MailFathom.Cli.Output;
using MailFathom.Domain.Access;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Writes what the credential commands print, so every one of them prints a credential the same way.</summary>
/// <remarks>
/// Nothing here prints a password and nothing here has one to print: the deployment publishes a method, what a
/// credential is resolved by where that is safe, a grant, a state, and two instants, and that is the whole of what
/// these commands ever hold. A password typed at the prompt reaches the request and nothing else — not the output, not
/// a confirmation, and not a refusal.
/// <para>
/// The one secret any of these commands prints is a key the deployment has just minted, which exists nowhere else and
/// is therefore unrecoverable the moment the terminal scrolls. It is printed with that said in the same breath, so
/// nobody discovers it by coming back for the value later.
/// </para>
/// </remarks>
internal static class OwnerCredentialOutput
{
    private const string WithheldLookup = "not published";

    private const string WholeSurface = "everything";

    /// <summary>Prints one owner's credentials as a listing.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="credentials">The credentials to print, in the order the deployment served them.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteListing(ICliConsole console, IReadOnlyList<OwnerCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(credentials);

        CliTable listing = new(
            "Credential",
            "Method",
            "Resolved by",
            "Grants",
            "State",
            "Provisioned",
            "Material changed");

        foreach (var credential in credentials)
        {
            listing.AddRow(
                $"{credential.Id:D}",
                credential.Method ?? "unreported",
                credential.Lookup ?? WithheldLookup,
                DescribeGrant(credential.Permissions),
                credential.Enabled ? "enabled" : "disabled",
                $"{credential.CreatedAt:u}",
                $"{credential.MaterialChangedAt:u}");
        }

        console.Write(listing);
    }

    /// <summary>Prints what provisioning produced, including the one value that exists only in this answer.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="method">The method the credential was provisioned for.</param>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="provisioned">What the deployment answered.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteProvisioned(
        ICliConsole console,
        OwnerCredentialMethod method,
        Guid owner,
        OwnerCredentialProvisioned provisioned)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(provisioned);

        console.WriteLine(
            $"Provisioned {method.Name} credential {provisioned.CredentialId:D} for owner {owner:D}.");

        WriteWhatTheClientPresents(console, method, provisioned.Lookup, provisioned.Key);
    }

    /// <summary>Prints what a rotation produced, including the one value that exists only in this answer.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="method">The method the credential carries.</param>
    /// <param name="credentialId">The credential that was rotated.</param>
    /// <param name="rotated">What the deployment answered.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteRotated(
        ICliConsole console,
        OwnerCredentialMethod method,
        Guid credentialId,
        OwnerCredentialRotated rotated)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(rotated);

        console.WriteLine(
            $"Replaced what {method.Name} credential {credentialId:D} is presented as. Anything still presenting the "
            + "previous material is refused from now on.");

        WriteWhatTheClientPresents(console, method, rotated.Lookup, rotated.Key);
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

    /// <summary>States what the owner's client has to hold, which differs by method and is the point of the command.</summary>
    private static void WriteWhatTheClientPresents(
        ICliConsole console,
        OwnerCredentialMethod method,
        string? lookup,
        string? key)
    {
        if (key is { Length: > 0 })
        {
            console.WriteLine(
                $"The client presents this key: {key}");
            console.WriteLine(
                "It is stored only as a digest, so nothing here or in the deployment can report it again. Copy it now.");

            return;
        }

        if (method == OwnerCredentialMethod.Password)
        {
            console.WriteLine(
                $"The owner signs in as '{lookup}' with the password you typed, which nothing here or in the "
                + "deployment can report back.");

            return;
        }

        if (method == OwnerCredentialMethod.PublicKey)
        {
            console.WriteLine($"The client's assertions must name this key in their 'kid' header: {lookup}");

            return;
        }

        console.WriteLine($"A validated token naming {lookup} now acts for that owner.");
    }

    private static string DescribeGrant(IReadOnlyList<string>? permissions) => permissions switch
    {
        null => "unreported",
        { Count: 0 } => "nothing",
        _ => permissions.Count == MailFathomPermission.PublishedFor(ProtectedSurface.Mail).Count
            ? WholeSurface
            : string.Join(", ", permissions),
    };
}
