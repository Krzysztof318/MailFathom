// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Commands.Configuration;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Credentials.SecretStores;
using MailFathom.Cli.Diagnostics;
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
/// <param name="Log">Where the record of this invocation is appended, or <see langword="null" /> to keep none — which is what a test not about the log wants.</param>
/// <param name="ReadEnvironmentVariable">Reads a variable of the shell the command was run from, or <see langword="null" /> for the process's own environment.</param>
/// <param name="OpenEditor">Runs the named editor over a file and waits for it, reporting whether it succeeded, or <see langword="null" /> to start a real process. A seam for the same reason the browser is one: a test drives an editing session by substituting what the operator would have typed into the buffer.</param>
internal sealed record CliContext(
    ICliConsole Console,
    CredentialStore Store,
    Func<Uri, StoredTransportTrust, DeploymentTransport> OpenTransport,
    Func<Uri, IMailboxRedirectAwaiter> AwaitRedirect,
    Func<Uri, bool> OpenBrowser,
    TimeProvider Clock,
    ICliInvocationLog? Log = null,
    Func<string, string?>? ReadEnvironmentVariable = null,
    Func<string, string, bool>? OpenEditor = null)
{
    /// <summary>Gets what this invocation turns out to have done, filled in by the layers that each know part of it.</summary>
    /// <remarks>
    /// <para>
    /// Timed from here rather than from the runner, so what is recorded is how long the operator waited rather than how
    /// long the parsed command took. A <c>with</c> expression keeps the same one rather than starting a second: the
    /// initializer runs in the constructor and a record's copy constructor copies the field instead of running it, which
    /// is what lets <see cref="CliRunner" /> swap the console for one that watches it without splitting the record in
    /// two.
    /// </para>
    /// <para>
    /// It is the one mutable thing this record holds, and it joins the synthesized equality like every other field — so
    /// two contexts are never equal, whatever they were built from. Nothing compares them, and there is no way to
    /// exclude it short of not storing it: a record's <c>Equals</c> reads the instance fields, so moving this behind a
    /// private one would change where it is written and not what is compared.
    /// </para>
    /// </remarks>
    internal CliInvocationRecord Invocation { get; } = new(Clock);

    /// <summary>Reads a variable of the shell this invocation was run from.</summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>Its value, or <see langword="null" /> when it is unset.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A seam rather than a direct read, because the process environment is shared by every test in an assembly that
    /// runs them in parallel: a test asserting what an invocation recorded would otherwise depend on whether the shell
    /// that started the run had turned the log off — which is a thing the documentation tells operators to do.
    /// </remarks>
    internal string? Variable(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return this.ReadEnvironmentVariable is { } read ? read(name) : Environment.GetEnvironmentVariable(name);
    }

    /// <summary>Runs the operator's editor over a file this command wrote, and waits for it.</summary>
    /// <param name="editor">The editor as the shell names it, which may carry arguments of its own.</param>
    /// <param name="path">The file to open.</param>
    /// <returns><see langword="true" /> when the editor ran and reported success.</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is <see langword="null" />, empty, or white space.</exception>
    internal bool Edit(string editor, string path) => this.OpenEditor is { } open
        ? open(editor, path)
        : OperatorEditor.Run(editor, path);

    /// <summary>Builds the context the command runs under in production.</summary>
    /// <returns>The context.</returns>
    internal static CliContext ForTerminal() => new(
        SystemCliConsole.ForTerminal(),
        new CredentialStore(
            CredentialStore.DefaultPath(),
            new TokenProtector(CredentialStore.DefaultKeyPath()),
            PlatformSecretStore.ForThisMachine()),
        DeploymentTransport.Open,
        redirectUri => new LoopbackRedirectAwaiter(redirectUri),
        WebBrowserLauncher.TryOpen,
        TimeProvider.System,
        new FileCliInvocationLog(FileCliInvocationLog.DefaultPath()));

    /// <summary>Reaches the deployment a command acts on, renewing a spent access token on the way.</summary>
    /// <returns>The access seam every command that sends a request goes through.</returns>
    internal DeploymentAccess Deployment() =>
        new(this.Store, this.OpenTransport, this.Clock, this.Invocation, this.Console);

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
