// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Cli;

/// <summary>Everything a command needs from outside itself.</summary>
/// <remarks>
/// One seam rather than three: a command reaches the terminal, the credential store, and the network through this, so a
/// test drives a command end to end by substituting them. Without it every command would open its own
/// <see cref="HttpClient" /> and reach the real profile directory, and nothing about argument handling or the sequence
/// of steps could be asserted.
/// </remarks>
/// <param name="Console">The terminal the command reads from and writes to.</param>
/// <param name="Store">Where the command remembers a credential.</param>
/// <param name="OpenTransport">Opens a transport aimed at one deployment; the caller disposes it.</param>
internal sealed record CliContext(
    ICliConsole Console,
    CredentialStore Store,
    Func<Uri, HttpClient> OpenTransport)
{
    /// <summary>How long any single request to a deployment may take.</summary>
    /// <remarks>
    /// A person is waiting at a terminal, so the bound is what keeps an unreachable host from looking like a hung
    /// command. It is generous enough for a deployment behind a slow link and short enough that a wrong address is
    /// reported rather than waited out.
    /// </remarks>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Builds the context the command runs under in production.</summary>
    /// <returns>The context.</returns>
    internal static CliContext ForTerminal() => new(
        new SystemCliConsole(),
        new CredentialStore(CredentialStore.DefaultPath()),
        OpenSystemTransport);

    /// <summary>Opens a transport aimed at one deployment.</summary>
    /// <remarks>
    /// Redirects are not followed. A redirect would move a request carrying a bearer credential to an address the
    /// operator never named, which is how a credential reaches a host that was never meant to see it.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The handler is handed to the HttpClient with disposeHandler: true, so the client the caller disposes disposes it; disposing it here would leave that client without a transport.")]
    private static HttpClient OpenSystemTransport(Uri endpoint) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false }, disposeHandler: true)
        {
            BaseAddress = endpoint,
            Timeout = RequestTimeout,
        };
}
