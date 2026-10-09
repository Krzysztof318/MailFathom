// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration.Users;
using MailFathom.Cli.Output;
using MailFathom.Domain.Access;

namespace MailFathom.Cli.Commands.Users;

/// <summary>Writes what the credential commands print, so every one of them prints a credential the same way.</summary>
/// <remarks>
/// Nothing here prints a password and nothing here has one to print: the deployment publishes a method, what a
/// credential is resolved by where that is safe, its narrowing and what it holds, a state, and two instants, and that is the whole of what
/// these commands ever hold. A password typed at the prompt reaches the request and nothing else — not the output, not
/// a confirmation, and not a refusal.
/// <para>
/// The one secret any of these commands prints is a key the deployment has just minted, which exists nowhere else and
/// is therefore unrecoverable the moment the terminal scrolls. It is printed with that said in the same breath, so
/// nobody discovers it by coming back for the value later.
/// </para>
/// </remarks>
internal static class UserCredentialOutput
{
    private const string WithheldLookup = "not published";

    /// <summary>Prints one user's credentials as a listing.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="credentials">The credentials to print, in the order the deployment served them.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteListing(ICliConsole console, IReadOnlyList<UserCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(credentials);

        CliTable listing = new(
            "Credential",
            "Method",
            "Resolved by",
            "Narrows to",
            "Holds",
            "Endpoints",
            "Accepted from",
            "State",
            "Provisioned",
            "Material changed");

        foreach (var credential in credentials)
        {
            listing.AddRow(
                $"{credential.Id:D}",
                credential.Method ?? "unreported",
                credential.Login ?? credential.Lookup ?? WithheldLookup,
                DescribeNarrowing(credential.Permissions),
                DescribeHeld(credential.EffectivePermissions),
                credential.Surfaces is { Count: > 0 } surfaces ? string.Join(", ", surfaces) : "unreported",
                credential.AllowedSourceNetworks is { Count: > 0 } networks ? string.Join(", ", networks) : "anywhere",
                credential.Enabled ? "enabled" : "disabled",
                $"{credential.CreatedAt:u}",
                $"{credential.MaterialChangedAt:u}");
        }

        console.Write(listing);
    }

    /// <summary>Prints what provisioning produced, including the one value that exists only in this answer.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="method">The method the credential was provisioned for.</param>
    /// <param name="user">The user the credential authenticates.</param>
    /// <param name="provisioned">What the deployment answered.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteProvisioned(
        ICliConsole console,
        UserCredentialMethod method,
        Guid user,
        UserCredentialProvisioned provisioned)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(provisioned);

        console.WriteLine(
            $"Provisioned {method.Name} credential {provisioned.CredentialId:D} for user {user:D}.");

        WriteWhatTheClientPresents(console, method, provisioned.Lookup, provisioned.Login, provisioned.Key);
    }

    /// <summary>Prints what a rotation produced, including the one value that exists only in this answer.</summary>
    /// <param name="console">The terminal to write to.</param>
    /// <param name="method">The method the credential carries.</param>
    /// <param name="credentialId">The credential that was rotated.</param>
    /// <param name="rotated">What the deployment answered.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static void WriteRotated(
        ICliConsole console,
        UserCredentialMethod method,
        Guid credentialId,
        UserCredentialRotated rotated)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(rotated);

        console.WriteLine(
            $"Replaced what {method.Name} credential {credentialId:D} is presented as. Anything still presenting the "
            + "previous material is refused from now on.");

        WriteWhatTheClientPresents(console, method, rotated.Lookup, rotated.Login, rotated.Key);
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

    /// <summary>States what the user's client has to hold, which differs by method and is the point of the command.</summary>
    private static void WriteWhatTheClientPresents(
        ICliConsole console,
        UserCredentialMethod method,
        string? lookup,
        string? login,
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

        if (method == UserCredentialMethod.Password)
        {
            console.WriteLine(
                $"The user signs in as '{login ?? lookup}' with the password you typed, which nothing here or in the "
                + "deployment can report back.");

            return;
        }

        if (method == UserCredentialMethod.PublicKey)
        {
            console.WriteLine($"The client's assertions must name this key in their 'kid' header: {lookup}");

            return;
        }

        console.WriteLine($"A validated token naming {lookup} now acts for that user.");
    }

    /// <summary>Describes what a credential narrows its user's grant to.</summary>
    /// <remarks>A credential naming nothing takes nothing away, which is a different statement from one naming the empty list, so the two are printed apart.</remarks>
    private static string DescribeNarrowing(IReadOnlyList<string>? permissions) => permissions switch
    {
        null => "nothing named",
        _ => DescribeHeld(permissions),
    };

    private static string DescribeHeld(IReadOnlyList<string>? permissions) => permissions switch
    {
        null => "unreported",
        { Count: 0 } => "nothing",
        _ when IsExactly(MailFathomPermission.All, permissions) => "everything",
        _ when IsExactly(MailFathomPermission.PublishedFor(ProtectedSurface.Mail), permissions) => "all mail",
        _ when IsExactly(MailFathomPermission.PublishedFor(ProtectedSurface.Administration), permissions) => "all administrative",
        _ => string.Join(", ", permissions),
    };

    /// <summary>Reports whether a list names exactly one whole set, which reads better as a word naming it than as a column of names.</summary>
    private static bool IsExactly(IReadOnlyList<MailFathomPermission> whole, IReadOnlyList<string> permissions) =>
        whole.Count == permissions.Count && whole.All(permission => permissions.Contains(permission.Name));
}
