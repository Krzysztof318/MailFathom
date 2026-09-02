// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace MailFathom.Cli.Authorization;

/// <summary>Catches the authorization redirect on a loopback address on this machine.</summary>
/// <remarks>
/// <para>
/// This is the whole reason the interactive flow works: the authorization server redirects the person's browser to
/// <c>http://127.0.0.1:&lt;port&gt;/</c>, which is an address only this machine can reach, so the authorization code
/// travels from the browser to this process without crossing a network. No paste, and nothing to mistype.
/// </para>
/// <para>
/// Loopback only, and refused otherwise. A prefix bound to a routable address would let anything that can reach this
/// host deliver a code, and would turn a redirect address the operator mistyped into an open port on their machine.
/// </para>
/// </remarks>
internal sealed class LoopbackRedirectAwaiter : IMailboxRedirectAwaiter
{
    private static readonly byte[] CompletionPage = Encoding.UTF8.GetBytes(
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>MailFathom</title></head>
        <body style="font-family: system-ui, sans-serif; margin: 4rem auto; max-width: 32rem;">
        <h1>Authorization received</h1>
        <p>You can close this tab and return to the terminal.</p>
        </body>
        </html>
        """);

    private readonly HttpListener listener;

    /// <summary>Binds the loopback address the redirect will arrive at.</summary>
    /// <param name="redirectUri">The address registered with the provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redirectUri" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the address is not a loopback address, or nothing can bind it.</exception>
    internal LoopbackRedirectAwaiter(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        if (!redirectUri.IsLoopback)
        {
            throw new CliFailure(
                $"'{redirectUri}' is not a loopback address, so this machine cannot receive the redirect. Register a http://127.0.0.1:<port>/ address with the provider, or use --mode manual.");
        }

        this.listener = new HttpListener();
        this.listener.Prefixes.Add(redirectUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");

        try
        {
            this.listener.Start();
        }
        catch (HttpListenerException failure)
        {
            throw new CliFailure(
                $"Nothing can listen at {redirectUri}: {failure.Message}. Another program may already hold the port; pass a different --redirect-uri, or use --mode manual.",
                failure);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// One redirect and no more. Anything arriving before it — a browser prefetch, a scan, a stale tab — is answered
    /// and ignored rather than treated as the authorization, because only a request carrying the anti-forgery value
    /// this run generated can be the one that was waited for, and that check belongs to the caller.
    /// </remarks>
    public async Task<MailboxRedirect> WaitForRedirectAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(this.listener.Stop);

        try
        {
            var context = await this.listener.GetContextAsync();
            var redirect = MailboxRedirect.FromQuery(context.Request.Url?.Query);

            await AnswerBrowserAsync(context.Response, cancellationToken);

            return redirect;
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping the listener is how the wait is cancelled, so the exception that produces is the cancellation.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose() => ((IDisposable)this.listener).Dispose();

    [SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "The command runs without a synchronization context, and root AGENTS.md refuses blanket ConfigureAwait in application code.")]
    private static async Task AnswerBrowserAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = CompletionPage.Length;

        await response.OutputStream.WriteAsync(CompletionPage, cancellationToken);

        response.Close();
    }
}
