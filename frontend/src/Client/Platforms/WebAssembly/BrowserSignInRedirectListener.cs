// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices.JavaScript;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization.Redirect;

namespace MailFathom.Client.Platforms.WebAssembly;

/// <summary>The browser head's way of putting the authorization page in front of the person and catching what comes back.</summary>
/// <remarks>
/// <para>
/// The one part of signing in that a browser cannot do the way a desktop window does. There is no socket to bind and no
/// process to start, so the redirect comes back to this application's own origin — in a window this document opened,
/// rather than by navigating this document away.
/// </para>
/// <para>
/// Navigating away is the obvious implementation and it is the wrong one. It destroys the page, and with it the proof
/// key and the anti-forgery value the returned code has to be redeemed against, which leaves only one place to put them
/// back: browser storage. Writing a PKCE verifier there would put the secret half of the pair somewhere every script on
/// the origin can read, for exactly the interval it is worth stealing — and the application writes nothing to browser
/// storage by design. Keeping the document alive keeps both values in memory, where they already were.
/// </para>
/// <para>
/// This type is compiled into the browser head alone: everything under <c>Platforms/WebAssembly/</c> is, which is what
/// lets it use the JavaScript interop the other heads have no counterpart for.
/// </para>
/// </remarks>
internal sealed partial class BrowserSignInRedirectListener : ISignInRedirectListener
{
    /// <summary>How often the window is asked whether the redirect has landed.</summary>
    /// <remarks>
    /// Short, because the window is closed as soon as the query is readable and the redirect lands on this
    /// application's own address — so the interval is how long the browser has to start a second copy of the
    /// application in a window nobody will see. Ten times a second costs nothing and keeps that below the point where
    /// anything is fetched.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private bool closed;

    /// <inheritdoc />
    /// <remarks>This application's own origin, which is what the authorization server has to have registered for it.</remarks>
    public Uri RedirectUri { get; } = new(Origin());

    /// <inheritdoc />
    public async Task<SignInRedirect> AuthorizeAsync(Uri authorizationUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUrl);

        if (!Open(authorizationUrl.AbsoluteUri))
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The browser blocked the sign-in window. Allow pop-ups for this site and try again.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var answer = Poll();

            if (answer.Length > 0)
            {
                return answer == "closed"
                    ? throw new DeploymentFailure(
                        DeploymentFailureReason.CredentialRefused,
                        "The sign-in window was closed before the sign-in finished.")
                    : SignInRedirect.FromQuery(answer);
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.closed)
        {
            return;
        }

        this.closed = true;

        Close();
    }

    [JSImport("globalThis.mailFathomSignIn.origin")]
    private static partial string Origin();

    [JSImport("globalThis.mailFathomSignIn.open")]
    private static partial bool Open(string authorizationUrl);

    [JSImport("globalThis.mailFathomSignIn.poll")]
    private static partial string Poll();

    [JSImport("globalThis.mailFathomSignIn.close")]
    private static partial void Close();
}

/// <summary>Opens the browser head's redirect listener, one per sign-in.</summary>
/// <remarks>
/// Registered by the composing host in place of the loopback factory <c>Client.Backend</c> registers by default. One
/// window at a time is the whole reason this is a factory rather than a service: a sign-in the person abandoned has to
/// give its window back before the next attempt asks for one.
/// </remarks>
internal sealed class BrowserSignInRedirectListenerFactory : ISignInRedirectListenerFactory
{
    /// <inheritdoc />
    public ISignInRedirectListener Open() => new BrowserSignInRedirectListener();
}
