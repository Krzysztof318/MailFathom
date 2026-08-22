// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization.Redirect;

/// <summary>One sign-in's way of putting the authorization page in front of the person and catching what comes back.</summary>
/// <remarks>
/// <para>
/// The one part of the flow that cannot be head-agnostic, which is why it is a port rather than a class. A desktop head
/// binds a loopback address and starts the platform's browser at it; the browser head opens a window on its own origin
/// and reads the redirect out of it. Everything else — discovery, the proof key, the exchange, what is done with the
/// token — is the same code on both, and lives in this assembly.
/// </para>
/// <para>
/// The redirect address is a property rather than an argument because the order matters: it goes into the authorization
/// request, so it has to be known before there is anything to open. Opening a listener therefore reserves whatever the
/// head needs to reserve, and disposing it releases that whether the sign-in completed, failed, or was abandoned.
/// </para>
/// </remarks>
public interface ISignInRedirectListener : IDisposable
{
    /// <summary>Gets the address the authorization server is asked to send the person back to.</summary>
    Uri RedirectUri { get; }

    /// <summary>Puts the authorization page in front of the person and waits for the redirect it produces.</summary>
    /// <param name="authorizationUrl">The address the person approves the sign-in at.</param>
    /// <param name="cancellationToken">Abandons the sign-in.</param>
    /// <returns>What the redirect carried, which the caller compares against the request before redeeming anything.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the person's browser could not be reached, or closed the sign-in without answering.</exception>
    Task<SignInRedirect> AuthorizeAsync(Uri authorizationUrl, CancellationToken cancellationToken);
}

/// <summary>Opens one sign-in's redirect listener, so a second sign-in is a second reservation rather than a shared one.</summary>
/// <remarks>
/// A factory because a listener holds a scarce thing for as long as it exists — a bound port on the desktop, a window
/// in the browser — and a sign-in that was abandoned must give that back before the next one asks for it. Registering
/// the listener itself as a service would make the whole application share one, and the second attempt after a
/// cancelled first would find it already spent.
/// </remarks>
public interface ISignInRedirectListenerFactory
{
    /// <summary>Reserves what this head needs to catch one redirect.</summary>
    /// <returns>The listener, which the caller disposes.</returns>
    /// <exception cref="DeploymentFailure">Thrown when nothing could be reserved to catch the redirect on.</exception>
    ISignInRedirectListener Open();
}
