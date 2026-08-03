// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Authorization;

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

    /// <summary>Builds a factory answering with an approval whose anti-forgery value is read when the wait begins.</summary>
    /// <param name="code">The authorization code the redirect carries.</param>
    /// <param name="readState">Reads the value the command generated, which only exists once the address has been shown.</param>
    /// <returns>The factory a context is built with.</returns>
    /// <remarks>
    /// The anti-forgery value is generated inside the authorizer and reaches the outside world only in the address the
    /// command prints, so a test that wants an approval accepted has to read it the way a browser does — from that
    /// address, after it is shown and before the redirect is answered.
    /// </remarks>
    internal static Func<Uri, IMailboxRedirectAwaiter> ApprovingWhenAsked(string code, Func<string> readState) =>
        Reacting(_ => Task.FromResult(new MailboxRedirect(code, readState(), Error: null)));

    /// <summary>Builds a factory answering with whatever the authorization server put in the redirect.</summary>
    /// <param name="redirect">What the redirect carries.</param>
    /// <returns>The factory a context is built with.</returns>
    internal static Func<Uri, IMailboxRedirectAwaiter> Answering(MailboxRedirect redirect) =>
        Reacting(_ => Task.FromResult(redirect));

    /// <summary>Builds a factory whose wait never completes until it is cancelled.</summary>
    /// <returns>The factory a context is built with.</returns>
    /// <remarks>
    /// A redirect that never arrives, which is what a person who abandons the sign-in looks like. It has to be a task
    /// that stays pending rather than one that is already cancelled: <see cref="Task.FromCanceled{TResult}" /> demands
    /// a token that is cancelled already, so building it from a live token throws where the wait was supposed to hang.
    /// </remarks>
    internal static Func<Uri, IMailboxRedirectAwaiter> Silent() =>
        Reacting(cancellationToken =>
        {
            TaskCompletionSource<MailboxRedirect> nothingArrives = new(TaskCreationOptions.RunContinuationsAsynchronously);

            cancellationToken.Register(() => nothingArrives.TrySetCanceled(cancellationToken));

            return nothingArrives.Task;
        });

    /// <inheritdoc />
    public Task<MailboxRedirect> WaitForRedirectAsync(CancellationToken cancellationToken) =>
        this.answer(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => this.WasDisposed = true;

    private static Func<Uri, IMailboxRedirectAwaiter> Reacting(Func<CancellationToken, Task<MailboxRedirect>> answer) =>
        redirectUri => new FakeMailboxRedirect(answer) { RequestedAddress = redirectUri };
}
