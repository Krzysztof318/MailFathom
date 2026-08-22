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
            var context = await this.listener.GetContextAsync().ConfigureAwait(false);
            var redirect = SignInRedirect.FromQuery(context.Request.Url?.Query);

            await AnswerBrowserAsync(context.Response, cancellationToken).ConfigureAwait(false);

            return redirect;
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

    private static async Task AnswerBrowserAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = CompletionPage.Length;

        await response.OutputStream.WriteAsync(CompletionPage, cancellationToken).ConfigureAwait(false);

        response.Close();
    }
}

/// <summary>Opens a loopback listener per sign-in, which is the default on every head that has sockets.</summary>
public sealed class LoopbackSignInRedirectListenerFactory : ISignInRedirectListenerFactory
{
    /// <inheritdoc />
    public ISignInRedirectListener Open() => new LoopbackSignInRedirectListener();
}
