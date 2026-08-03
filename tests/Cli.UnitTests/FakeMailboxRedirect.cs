// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.UnitTests;

/// <summary>The authorization redirect a command meets, without a socket to bind or a browser to drive.</summary>
/// <remarks>
/// The real awaiter listens on a loopback address, which a unit test may not do. Substituting it is what makes the
/// command's own decisions — a refused authorization, a mismatched anti-forgery value, a redirect carrying neither code
/// nor error — reachable, and those are the decisions worth asserting.
/// </remarks>
internal sealed class FakeMailboxRedirect : IMailboxRedirectAwaiter
{
    private readonly Func<CancellationToken, Task<MailboxRedirect>> answer;

    private FakeMailboxRedirect(Func<CancellationToken, Task<MailboxRedirect>> answer) => this.answer = answer;

    /// <summary>Gets the address this awaiter was asked to listen on.</summary>
    internal Uri? RequestedAddress { get; private init; }

    /// <summary>Gets whether the awaiter was disposed, which is how the real listener stops.</summary>
    internal bool WasDisposed { get; private set; }

    /// <summary>Builds a factory answering with an approved authorization.</summary>
    /// <param name="code">The authorization code the redirect carries.</param>
    /// <param name="state">The anti-forgery value the redirect echoes.</param>
    /// <returns>The factory a context is built with.</returns>
    internal static Func<Uri, IMailboxRedirectAwaiter> Approving(string code, string state) =>
        Answering(new MailboxRedirect(code, state, Error: null));

    /// <summary>Builds a factory answering with whatever the authorization server put in the redirect.</summary>
    /// <param name="redirect">What the redirect carries.</param>
    /// <returns>The factory a context is built with.</returns>
    internal static Func<Uri, IMailboxRedirectAwaiter> Answering(MailboxRedirect redirect) =>
        Reacting(_ => Task.FromResult(redirect));

    /// <summary>Builds a factory whose wait never completes, so a cancelled command can be observed.</summary>
    /// <returns>The factory a context is built with.</returns>
    internal static Func<Uri, IMailboxRedirectAwaiter> Silent() =>
        Reacting(cancellationToken => Task.FromCanceled<MailboxRedirect>(cancellationToken));

    /// <inheritdoc />
    public Task<MailboxRedirect> WaitForRedirectAsync(CancellationToken cancellationToken) =>
        this.answer(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => this.WasDisposed = true;

    private static Func<Uri, IMailboxRedirectAwaiter> Reacting(Func<CancellationToken, Task<MailboxRedirect>> answer) =>
        redirectUri => new FakeMailboxRedirect(answer) { RequestedAddress = redirectUri };
}
