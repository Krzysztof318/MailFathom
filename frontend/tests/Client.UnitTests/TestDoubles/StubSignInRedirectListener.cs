// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization.Redirect;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A redirect listener that answers with a scripted redirect instead of opening anything.</summary>
/// <remarks>
/// This is the seam that keeps the sign-in testable at all. The one head-specific step — putting the authorization page
/// in front of somebody and catching what comes back — is a port for exactly this reason, so the whole of the flow
/// around it is asserted without a browser, a socket, or a visual tree.
/// </remarks>
internal sealed class StubSignInRedirectListener : ISignInRedirectListener, ISignInRedirectListenerFactory
{
    /// <summary>The authorization code every scripted approval comes back with.</summary>
    internal const string ApprovedCode = "the-code";

    private readonly Func<Uri, SignInRedirect> answer;

    /// <summary>Initializes a listener answering from a script.</summary>
    /// <param name="answer">Produces the redirect for the authorization address it was given.</param>
    internal StubSignInRedirectListener(Func<Uri, SignInRedirect> answer) => this.answer = answer;

    /// <summary>Gets the authorization address the sign-in built, or <see langword="null" /> where none was opened.</summary>
    internal Uri? OpenedAuthorizationUrl { get; private set; }

    /// <summary>Gets whether the listener was released.</summary>
    internal bool Disposed { get; private set; }

    /// <inheritdoc />
    public Uri RedirectUri { get; } = new("http://127.0.0.1:49152/");

    /// <summary>Answers the way a completed approval does, echoing the value the sign-in actually generated.</summary>
    internal static SignInRedirect Approved(Uri authorizationUrl) =>
        new(ApprovedCode, StateOf(authorizationUrl), null);

    /// <summary>Answers the way an authorization server refusing the request does.</summary>
    internal static SignInRedirect Refused(Uri authorizationUrl) =>
        new(null, StateOf(authorizationUrl), "access_denied");

    /// <summary>Answers as an approval belonging to some exchange this process never started.</summary>
    internal static SignInRedirect ApprovedForSomeOtherRequest(Uri _) =>
        new(ApprovedCode, "a-state-this-run-never-generated", null);

    /// <summary>Answers as a refusal belonging to some exchange this process never started.</summary>
    internal static SignInRedirect RefusedForSomeOtherRequest(Uri _) =>
        new(null, "a-state-this-run-never-generated", "access_denied");

    /// <summary>Reads the <c>state</c> parameter out of an authorization address the sign-in built.</summary>
    /// <param name="authorizationUrl">The address.</param>
    /// <returns>The value, or <see langword="null" /> where the address carried none.</returns>
    internal static string? StateOf(Uri authorizationUrl) => SignInRedirect.FromQuery(authorizationUrl.Query).State;

    /// <inheritdoc />
    public ISignInRedirectListener Open() => this;

    /// <inheritdoc />
    public Task<SignInRedirect> AuthorizeAsync(Uri authorizationUrl, CancellationToken cancellationToken)
    {
        this.OpenedAuthorizationUrl = authorizationUrl;

        return Task.FromResult(this.answer(authorizationUrl));
    }

    /// <inheritdoc />
    public void Dispose() => this.Disposed = true;
}
