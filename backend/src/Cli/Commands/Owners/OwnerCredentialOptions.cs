// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Domain.Access;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>The options the credential commands share, and how an owner and a method are settled on.</summary>
/// <remarks>
/// <para>
/// There is no password option here and there never will be one. A value written on a command line reaches the shell's
/// history, the process table, and the log of whatever supervisor started the command, so the password is read through
/// <see cref="ICliConsole.ReadSecret" /> instead — which prompts without echoing when somebody is at the terminal and
/// reads one line when the input is a pipe, so a script supplies one without it ever being an argument. A public key
/// is read from a file for a different reason: it is not secret, and it is several lines long.
/// </para>
/// <para>
/// The owner is optional, because the deployment this serves holds one. A command given no owner asks which owners
/// exist and acts on the single one, and refuses rather than guessing where there are several — so the ordinary
/// invocation names nothing and the ambiguous one is told exactly what to add.
/// </para>
/// <para>
/// The method is not optional, and is the same word an endpoint section writes to say it accepts one. A command that
/// guessed from which other options were present would make a mistyped option name the difference between provisioning
/// one kind of credential and another.
/// </para>
/// </remarks>
internal static class OwnerCredentialOptions
{
    /// <summary>Builds the option naming which owner a command acts for.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid?> Owner() => new("--owner")
    {
        Description =
            "The owner to act for, by the identifier the deployment gave their record. Optional where the deployment holds one owner.",
    };

    /// <summary>Builds the option naming which credential a command acts on.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Credential() => new("--id")
    {
        Description = "The credential to act on, by the identifier the deployment gave it.",
        Required = true,
    };

    /// <summary>Builds the option naming which method the credential is presented by.</summary>
    /// <returns>The option.</returns>
    internal static Option<string> Method() => new("--method")
    {
        Description =
            $"How the credential is presented: {PublishedMethodNames()}. The same word an endpoint section writes to accept the method.",
        Required = true,
    };

    /// <summary>Builds the option naming the username a password credential is provisioned under.</summary>
    /// <returns>The option.</returns>
    /// <remarks>An option rather than a prompt, because a username is not a secret: it is written down beside the address the owner signs in to, and a deployment provisioning several from a script needs it to be an argument.</remarks>
    internal static Option<string> Username() => new("--username")
    {
        Description =
            "The name the owner will sign in with, for '--method password'. Folded to its canonical lower-case form by the deployment.",
    };

    /// <summary>Builds the option naming the file the client's public key is read from.</summary>
    /// <returns>The option.</returns>
    /// <remarks>A file rather than a value, because a public key is several lines of base64 between two delimiters and a shell that folded one onto a single argument would send something the deployment cannot read back.</remarks>
    internal static Option<FileInfo> PublicKeyFile() => new("--public-key-file")
    {
        Description = "The file holding the client's public key, for '--method public-key'.",
    };

    /// <summary>Builds the option naming the authorization server a mapped subject was issued by.</summary>
    /// <returns>The option.</returns>
    internal static Option<string> Issuer() => new("--issuer")
    {
        Description =
            "The authorization server's issuer identifier, for '--method oauth-subject'. Write it exactly as the server publishes it and as the endpoint section configures it.",
    };

    /// <summary>Builds the option naming the subject the mapped owner acts as.</summary>
    /// <returns>The option.</returns>
    internal static Option<string> Subject() => new("--subject")
    {
        Description = "The 'sub' that server issues for the person, for '--method oauth-subject'.",
    };

    /// <summary>Builds the repeatable option naming what the credential may do.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Written once per permission rather than as one delimited value, so a name is never split by whichever separator a shell decided to expand.</remarks>
    internal static Option<string[]> Permission() => new("--permission")
    {
        Description =
            "A permission the credential holds, repeatable. Written nowhere at all, the credential holds everything the mail surface publishes.",
    };

    /// <summary>Builds the flag stating that the credential authenticates and may do nothing.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Separate from an empty <c>--permission</c>, because a repeatable option written zero times is indistinguishable from one nobody wrote — and those are the two opposite grants. Stating the empty one takes a word of its own so it cannot be reached by accident.</remarks>
    internal static Option<bool> NoPermissions() => new("--no-permissions")
    {
        Description =
            "Provision the credential granting nothing, which authenticates and reaches no tool. Refused beside '--permission'.",
    };

    /// <summary>Settles on the method a command acts under, refusing a name this repository does not publish.</summary>
    /// <param name="written">The method the invocation named.</param>
    /// <returns>The method.</returns>
    /// <exception cref="CliFailure">Thrown when the name is not one of the published methods.</exception>
    /// <remarks>Parsed here rather than sent as written, so a misspelling is answered before a request is made and the answer lists what could have been meant.</remarks>
    internal static OwnerCredentialMethod ResolveMethod(string? written) =>
        OwnerCredentialMethod.TryParse(written, out var method)
            ? method
            : throw new CliFailure(
                $"'{written}' is not a credential method this deployment publishes. Write one of {PublishedMethodNames()}.");

    /// <summary>Settles on the grant a command provisions, refusing the two ways of stating it at once.</summary>
    /// <param name="permissions">The permissions the invocation named, which may be none.</param>
    /// <param name="noPermissions">Whether the invocation stated that the credential grants nothing.</param>
    /// <returns>The permission names to send, or <see langword="null" /> to hold the whole mail surface.</returns>
    /// <exception cref="CliFailure">Thrown when the invocation both named permissions and stated that there are none.</exception>
    internal static IReadOnlyList<string>? ResolveGrant(string[]? permissions, bool noPermissions)
    {
        if (permissions is { Length: > 0 } named)
        {
            return noPermissions
                ? throw new CliFailure(
                    "The invocation both names permissions and says there are none. Drop '--no-permissions' to grant "
                    + "what was named, or drop the '--permission' arguments to grant nothing.")
                : named;
        }

        return noPermissions ? [] : null;
    }

    /// <summary>Reads a client's public key out of the file the invocation named.</summary>
    /// <param name="file">The file the invocation named, or <see langword="null" /> where it named none.</param>
    /// <returns>The key as written.</returns>
    /// <exception cref="CliFailure">Thrown when no file was named, the file is not there, or it cannot be read.</exception>
    /// <remarks>Read here rather than streamed to the deployment, because it is a few hundred bytes and because a file that is not there is worth answering before a request is made.</remarks>
    internal static string ReadPublicKey(FileInfo? file)
    {
        if (file is null)
        {
            throw new CliFailure(
                "This method verifies assertions a client signs, so it needs that client's public key. Pass "
                + "'--public-key-file' with the file holding it.");
        }

        try
        {
            return File.ReadAllText(file.FullName);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The public key file '{file.FullName}' could not be read: {failure.Message}", failure);
        }
    }

    /// <summary>Settles on the owner a command acts for, asking the deployment where the invocation named none.</summary>
    /// <param name="deployment">The client already reaching the deployment.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <param name="requestedOwner">The owner the invocation named, or <see langword="null" /> where it named none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The owner to act for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deployment" /> or <paramref name="token" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment holds no owner at all, or holds several and the invocation named none.</exception>
    /// <remarks>
    /// A named owner is used as written and never checked against the roster first: the deployment refuses an owner it
    /// holds no record for and says so, and a lookup here would only decide the same thing one request earlier while
    /// telling the operator which identifiers exist. The empty identifier is a stated owner like any other — an unset
    /// script variable expands to one, and reading it as "no owner was named" would act on the single owner a
    /// deployment happens to hold instead of refusing an invocation that named nobody.
    /// </remarks>
    internal static async Task<Guid> ResolveOwnerAsync(
        AdminApiClient deployment,
        string token,
        Guid? requestedOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(token);

        if (requestedOwner is { } named)
        {
            return named;
        }

        var roster = await deployment.ReadOwnersAsync(token, cancellationToken);

        return roster.Owners switch
        {
            [var only] => only,
            null or [] => throw new CliFailure(
                "The deployment holds no owner records, so there is nobody to provision a credential for. An owner record "
                + "is written when the deployment first composes its settings; check that it started successfully."),
            var several => throw new CliFailure(
                $"The deployment holds {several.Count} owners, so which one this acts for has to be said. Pass --owner "
                + $"with one of: {string.Join(", ", several.Select(owner => owner.ToString("D", null)))}"),
        };
    }

    private static string PublishedMethodNames() =>
        string.Join(", ", OwnerCredentialMethod.All.Select(method => $"'{method.Name}'"));
}
