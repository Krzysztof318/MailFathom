// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Commands;

namespace MailFathom.Cli.Transport;

/// <summary>Settles whether a sign-in may talk to a deployment over a connection nothing protects.</summary>
/// <remarks>
/// <para>
/// An <c>http://</c> address is taken as written, and the transport does not follow redirects, so a deployment's
/// HTTP-to-HTTPS redirect never upgrades the connection in flight — the credential is already on the wire by the time
/// the redirect is read. That is why the question is asked from the address alone, before anything is sent, and why the
/// deployment's own answer cannot stand in for it.
/// </para>
/// <para>
/// It is asked at <c>login</c> and nowhere else. A later command acts on a decision already taken, and a tool that asks
/// the same question on every invocation trains an operator to answer it without reading it.
/// </para>
/// </remarks>
internal static class ClearTextDecision
{
    /// <summary>The switch a sign-in with nobody at the terminal states the answer with.</summary>
    internal const string AllowanceOption = "--allow-clear-text";

    /// <summary>Settles whether this sign-in accepts an unprotected connection to the address it names.</summary>
    /// <param name="console">The terminal the question is asked on.</param>
    /// <param name="address">The address the operator named.</param>
    /// <param name="allowedUpFront">Whether the invocation stated the answer with <see cref="AllowanceOption" />.</param>
    /// <returns><see langword="true" /> when the profile is being signed in over an unprotected connection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the operator refused, or when nobody could be asked and the invocation did not state the answer.</exception>
    internal static bool Settle(ICliConsole console, Uri address, bool allowedUpFront)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(address);

        if (address.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (allowedUpFront)
        {
            return true;
        }

        if (!console.CanConfirm)
        {
            throw new CliFailure(
                $"{address.GetLeftPart(UriPartial.Authority)} is reached over HTTP, so the credential and every later request would cross the network unprotected, and there is no terminal to ask on. Sign in to an https:// address instead, or pass {AllowanceOption} to accept an unprotected connection to this deployment.");
        }

        console.WriteWarning(string.Empty);
        console.WriteWarning($"{address.GetLeftPart(UriPartial.Authority)} is an HTTP address, so nothing protects this connection.");
        console.WriteWarning("The credential you are about to present, and every later request from this profile, cross the network in clear text.");
        console.WriteWarning("A redirect the deployment might send to an https:// address would not change that: the credential is already on the wire by then.");
        console.WriteWarning(string.Empty);

        return console.Confirm("Sign in over an unprotected connection anyway? [y/N]: ")
            ? true
            : throw new CliFailure(
                $"Transport protection was refused, so nothing was signed in to and nothing was stored. Sign in to an https:// address, or run '{CliRootCommand.CommandName} login' again and accept the unprotected connection.");
    }
}
