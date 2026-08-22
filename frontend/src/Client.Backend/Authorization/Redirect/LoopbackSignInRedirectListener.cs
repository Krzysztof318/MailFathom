// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MailFathom.Client.Backend.Authorization.Redirect;

/// <summary>Catches the authorization redirect on a loopback address on this machine, the way <c>mfctl login</c> does.</summary>
/// <remarks>
/// <para>
/// The authorization server redirects the person's browser to <c>http://127.0.0.1:&lt;port&gt;/</c>, which is an address
/// only this machine can reach, so the authorization code travels from the browser to this process without crossing a
/// network. Nothing is pasted and nothing is mistyped.
/// </para>
/// <para>
/// Loopback only. A prefix bound to a routable address would let anything that can reach this host deliver a code, and
/// would turn a redirect address into an open port on somebody's machine.
/// </para>
/// <para>
/// This is the desktop head's implementation and the default one. It is unusable in a browser — a WebAssembly page
/// binds no sockets and starts no processes — which is why <see cref="ISignInRedirectListener" /> is a port at all and
/// why the browser head supplies its own.
/// </para>
/// </remarks>
public sealed class LoopbackSignInRedirectListener : ISignInRedirectListener
{
    private static readonly byte[] CompletionPage = Encoding.UTF8.GetBytes(
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>MailFathom</title></head>
        <body style="font-family: system-ui, sans-serif; margin: 4rem auto; max-width: 32rem;">
        <h1>Signed in</h1>
        <p>You can close this tab and return to MailFathom.</p>
        </body>
        </html>
        """);

    /// <summary>What the browser tab is left showing when the person refused the sign-in or the server did.</summary>
    /// <remarks>
    /// It says what happened and nothing about why. The refusal code arrived through the person's browser from a
    /// machine this process does not own, and writing it back into this page would put an attacker's words on a page
    /// MailFathom appears to have authored.
    /// </remarks>
    private static readonly byte[] RefusalPage = Encoding.UTF8.GetBytes(
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>MailFathom</title></head>
        <body style="font-family: system-ui, sans-serif; margin: 4rem auto; max-width: 32rem;">
        <h1>Not signed in</h1>
        <p>The sign-in was refused or dismissed. You can close this tab and try again from MailFathom.</p>
        </body>
        </html>
        """);

    private readonly HttpListener listener;

    /// <summary>Binds a free loopback port and prepares to answer one redirect on it.</summary>
    /// <exception cref="DeploymentFailure">Thrown when nothing on this machine can listen for the redirect.</exception>
    public LoopbackSignInRedirectListener()
    {
        var port = ReserveLoopbackPort();

        this.RedirectUri = new Uri(
            string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/"));

        this.listener = new HttpListener();
        this.listener.Prefixes.Add(this.RedirectUri.AbsoluteUri);

        try
        {
            this.listener.Start();
        }
        catch (Exception failure) when (failure is HttpListenerException or PlatformNotSupportedException)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "Nothing on this machine can listen for the sign-in to come back, so the browser sign-in cannot be started here.",
                failure);
        }
    }

    /// <inheritdoc />
    public Uri RedirectUri { get; }

    /// <inheritdoc />
    /// <remarks>
    /// One redirect and no more. Anything arriving before it — a browser prefetch, a scan, a stale tab — is answered and
    /// ignored rather than treated as the authorization, because only a request echoing the value this sign-in
    /// generated can be the one that was waited for, and that comparison belongs to the caller.
    /// </remarks>
    public async Task<SignInRedirect> AuthorizeAsync(Uri authorizationUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUrl);

        OpenInBrowser(authorizationUrl);

        using var registration = cancellationToken.Register(this.listener.Stop);

        try
        {
            while (true)
            {
                var context = await this.listener.GetContextAsync().ConfigureAwait(false);
                var redirect = SignInRedirect.FromQuery(context.Request.Url?.Query);

                await AnswerBrowserAsync(context.Response, redirect, cancellationToken).ConfigureAwait(false);

                if (redirect.CarriesAnAnswer)
                {
                    return redirect;
                }
            }
        }
        catch (Exception failure)
            when (failure is HttpListenerException or ObjectDisposedException
                && cancellationToken.IsCancellationRequested)
        {
            // Stopping the listener is how the wait is cancelled, so the exception that produces is the cancellation.
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose() => ((IDisposable)this.listener).Dispose();

    /// <summary>Finds a loopback port nothing currently holds.</summary>
    /// <remarks>
    /// Asking the operating system for port zero and reading back what it chose, which is the only way to learn a free
    /// port. There is a window between releasing it here and binding it below in which something else could take it;
    /// that arrives as the refusal above rather than as a wrong port, and a person's answer to it is to try again.
    /// </remarks>
    private static int ReserveLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);

        try
        {
            probe.Start();

            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        catch (Exception failure) when (failure is SocketException or PlatformNotSupportedException)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "No loopback port could be reserved for the sign-in to come back to.",
                failure);
        }
    }

    /// <summary>Starts the platform's own browser at the authorization address.</summary>
    /// <remarks>
    /// <c>UseShellExecute</c> is what hands the address to whatever the person has configured, rather than to a browser
    /// this application picked for them. A machine with nothing configured — a bare server, a locked-down desktop — is
    /// a machine where this reports that the page has to be opened by hand.
    /// </remarks>
    private static void OpenInBrowser(Uri authorizationUrl)
    {
        try
        {
            using var browser = Process.Start(
                new ProcessStartInfo(authorizationUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or PlatformNotSupportedException or InvalidOperationException)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "No browser could be started on this machine, so the sign-in page could not be opened.",
                failure);
        }
    }

    /// <summary>Tells the person in the browser what this process just read, rather than what it hoped to read.</summary>
    /// <remarks>
    /// The tab is the only surface the person is looking at while the application waits, so a refusal that answered
    /// with the completion page would say the sign-in succeeded at the moment the caller is about to report that it did
    /// not. A request carrying nothing was never part of this flow and is answered as the nothing it asked for.
    /// </remarks>
    private static async Task AnswerBrowserAsync(
        HttpListenerResponse response,
        SignInRedirect redirect,
        CancellationToken cancellationToken)
    {
        var page = redirect switch
        {
            { Error: not null } => RefusalPage,
            { CarriesAnAnswer: true } => CompletionPage,
            _ => null,
        };

        response.StatusCode = (int)(page is null ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        response.ContentLength64 = page?.Length ?? 0;

        if (page is not null)
        {
            response.ContentType = "text/html; charset=utf-8";

            await response.OutputStream.WriteAsync(page, cancellationToken).ConfigureAwait(false);
        }

        response.Close();
    }
}

/// <summary>Opens a loopback listener per sign-in, which is the default on every head that has sockets.</summary>
public sealed class LoopbackSignInRedirectListenerFactory : ISignInRedirectListenerFactory
{
    /// <inheritdoc />
    public ISignInRedirectListener Open() => new LoopbackSignInRedirectListener();
}
