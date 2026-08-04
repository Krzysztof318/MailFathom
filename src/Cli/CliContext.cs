// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;

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
/// <param name="OpenTransport">Opens a transport aimed at one deployment; the caller disposes it.</param>
/// <param name="AwaitRedirect">Binds the loopback address an authorization redirect arrives at; the caller disposes it.</param>
/// <param name="OpenBrowser">Opens an address in this machine's browser, reporting whether the attempt was made.</param>
/// <param name="Clock">Decides whether a stored access token is still usable, and paces a device sign-in's polling.</param>
internal sealed record CliContext(
    ICliConsole Console,
    CredentialStore Store,
    Func<Uri, HttpClient> OpenTransport,
    Func<Uri, IMailboxRedirectAwaiter> AwaitRedirect,
    Func<Uri, bool> OpenBrowser,
    TimeProvider Clock)
{
    /// <summary>How long any single request to a deployment may take.</summary>
    /// <remarks>
    /// A person is waiting at a terminal, so the bound is what keeps an unreachable host from looking like a hung
    /// command. It is generous enough for a deployment behind a slow link and short enough that a wrong address is
    /// reported rather than waited out.
    /// </remarks>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The largest response the command reads from anything, beyond which the request fails.</summary>
    /// <remarks>
    /// Every document the command fetches is a few kilobytes: a session, a protected resource metadata document, an
    /// authorization server's discovery document, a token response. The limit exists because two of the machines
    /// answering are not the deployment's — an authorization server is reached during a sign-in, and a mistyped or
    /// hijacked address is reached by definition — and none of them should be able to make the command buffer an
    /// unbounded body. The same number the service bounds its own metadata retrieval by, for the same reason.
    /// </remarks>
    internal const int ResponseSizeLimitInBytes = 256 * 1024;

    /// <summary>Builds the context the command runs under in production.</summary>
    /// <returns>The context.</returns>
    internal static CliContext ForTerminal() => new(
        new SystemCliConsole(),
        new CredentialStore(CredentialStore.DefaultPath(), new TokenProtector(CredentialStore.DefaultKeyPath())),
        OpenSystemTransport,
        redirectUri => new LoopbackRedirectAwaiter(redirectUri),
        WebBrowserLauncher.TryOpen,
        TimeProvider.System);

    /// <summary>Reaches the deployment a command acts on, renewing a spent access token on the way.</summary>
    /// <returns>The access seam every command that sends a request goes through.</returns>
    internal DeploymentAccess Deployment() => new(this.Store, this.OpenTransport, this.Clock);

    /// <summary>Opens a transport aimed at one address.</summary>
    /// <remarks>
    /// Three bounds, and each answers a different way a remote machine could misbehave. Redirects are not followed,
    /// because a redirect would move a request carrying a bearer credential to an address the operator never named. The
    /// timeout keeps an unresponsive host from looking like a hung command. The buffer limit stops any of the machines
    /// the command talks to from answering with an unbounded body — which matters most during a sign-in, where one of
    /// them is an authorization server rather than the deployment.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The handler is handed to the HttpClient with disposeHandler: true, so the client the caller disposes disposes it; disposing it here would leave that client without a transport.")]
    private static HttpClient OpenSystemTransport(Uri endpoint) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true)
        {
            BaseAddress = endpoint,
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = ResponseSizeLimitInBytes,
        };
}
