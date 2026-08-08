// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Transport;

namespace MailFathom.Cli;

/// <summary>Everything a command needs from outside itself.</summary>
/// <remarks>
/// One seam rather than three: a command reaches the terminal, the credential store, and the network through this, so a
/// test drives a command end to end by substituting them. Without it every command would open its own
/// <see cref="HttpClient" /> and reach the real profile directory, and nothing about argument handling or the sequence
/// of steps could be asserted.
/// </remarks>
/// <param name="Console">The terminal the command reads from and writes to.</param>
/// <param name="Store">Where the command remembers the deployments signed in to.</param>
/// <param name="OpenTransport">Opens a transport aimed at one address, accepting what the given trust allows there; the caller disposes it.</param>
/// <param name="AwaitRedirect">Binds the loopback address an authorization redirect arrives at; the caller disposes it.</param>
/// <param name="OpenBrowser">Opens an address in this machine's browser, reporting whether the attempt was made.</param>
/// <param name="Clock">Decides whether a stored access token is still usable, and paces a device sign-in's polling.</param>
internal sealed record CliContext(
    ICliConsole Console,
    CredentialStore Store,
    Func<Uri, StoredTransportTrust, DeploymentTransport> OpenTransport,
    Func<Uri, IMailboxRedirectAwaiter> AwaitRedirect,
    Func<Uri, bool> OpenBrowser,
    TimeProvider Clock)
{
    /// <summary>Builds the context the command runs under in production.</summary>
    /// <returns>The context.</returns>
    internal static CliContext ForTerminal() => new(
        new SystemCliConsole(),
        new CredentialStore(CredentialStore.DefaultPath(), new TokenProtector(CredentialStore.DefaultKeyPath())),
        DeploymentTransport.Open,
        redirectUri => new LoopbackRedirectAwaiter(redirectUri),
        WebBrowserLauncher.TryOpen,
        TimeProvider.System);

    /// <summary>Reaches the deployment a command acts on, renewing a spent access token on the way.</summary>
    /// <returns>The access seam every command that sends a request goes through.</returns>
    internal DeploymentAccess Deployment() => new(this.Store, this.OpenTransport, this.Clock);

    /// <summary>Opens a transport aimed at an address no profile has accepted anything about.</summary>
    /// <param name="address">The address, which is an authorization server rather than a deployment.</param>
    /// <returns>The transport, which the caller disposes.</returns>
    /// <remarks>
    /// A profile's pin belongs to the deployment it was taken at and to nothing else, so a request to an authorization
    /// server goes out under ordinary chain validation. Reusing the deployment's transport for it would apply the pin to
    /// a host it says nothing about, and every such request would be refused for presenting the wrong certificate.
    /// </remarks>
    internal DeploymentTransport OpenUnpinnedTransport(Uri address) =>
        this.OpenTransport(address, StoredTransportTrust.Protected);
}
