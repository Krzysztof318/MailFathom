// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization.Redirect;

namespace MailFathom.Client.Backend.Authorization;

/// <summary>Signs a person in to their deployment, and holds the result for as long as the application runs.</summary>
/// <remarks>
/// <para>
/// Authorization code with PKCE, which is the grant <c>mfctl</c> already performs against the administrative surface
/// and for the same reason: this application is a public client in both heads it runs as, so it holds no client secret
/// and every grant is bound by a proof key instead. The <c>resource</c> parameter of RFC 8707 names the deployment, so
/// the issued token's audience is the endpoint being signed in to rather than anything else that server protects.
/// </para>
/// <para>
/// What differs between the heads is one step — putting the authorization page in front of the person and catching what
/// comes back — and that is <see cref="ISignInRedirectListener" />. Everything here runs identically on a desktop
/// window and in a browser tab.
/// </para>
/// <para>
/// No refresh token is asked for and none is kept. The session lasts as long as the token the authorization server
/// issued, and then the person signs in again; persisting anything that would outlive the process is a separate
/// decision with its own privacy reasoning, and this class deliberately does not take it.
/// </para>
/// </remarks>
public sealed class DeploymentSignIn
{
    private readonly HttpClient deployment;
    private readonly HttpClient authorizationServer;
    private readonly DeploymentOptions options;
    private readonly ISignInRedirectListenerFactory listeners;
    private readonly AccessTokenStore tokens;

    /// <summary>Initializes the sign-in over the transports, the head's redirect listener, and the token store.</summary>
    /// <param name="transports">Supplies the two configured clients this flow talks to.</param>
    /// <param name="options">Which deployment, and as which registered client.</param>
    /// <param name="listeners">How this head catches the redirect.</param>
    /// <param name="tokens">Where the issued token is held for this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DeploymentSignIn(
        IHttpClientFactory transports,
        DeploymentOptions options,
        ISignInRedirectListenerFactory listeners,
        AccessTokenStore tokens)
        : this(
            (transports ?? throw new ArgumentNullException(nameof(transports))).CreateClient(DeploymentHttpClients.Deployment),
            transports.CreateClient(DeploymentHttpClients.AuthorizationServer),
            options,
            listeners,
            tokens)
    {
    }

    /// <summary>Initializes the sign-in over transports supplied directly, which is how a test stubs them.</summary>
    /// <param name="deployment">Aimed at the deployment.</param>
    /// <param name="authorizationServer">Aimed at the authorization server.</param>
    /// <param name="options">Which deployment, and as which registered client.</param>
    /// <param name="listeners">How this head catches the redirect.</param>
    /// <param name="tokens">Where the issued token is held for this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DeploymentSignIn(
        HttpClient deployment,
        HttpClient authorizationServer,
        DeploymentOptions options,
        ISignInRedirectListenerFactory listeners,
        AccessTokenStore tokens)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(authorizationServer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(tokens);

        this.deployment = deployment;
        this.authorizationServer = authorizationServer;
        this.options = options;
        this.listeners = listeners;
        this.tokens = tokens;
    }

    /// <summary>Runs the whole sign-in and keeps what it produced.</summary>
    /// <param name="cancellationToken">Abandons the sign-in, releasing whatever the head reserved for the redirect.</param>
    /// <returns>A task completing once the token is held.</returns>
    /// <exception cref="DeploymentFailure">Thrown when discovery, the person's approval, or the exchange did not produce a token.</exception>
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await new DeploymentAuthorizationDiscovery(this.deployment, this.authorizationServer)
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);

        using var listener = this.listeners.Open();

        var pending = BuildAuthorization(authorization, this.options.ClientId, listener.RedirectUri);

        var redirect = await listener
            .AuthorizeAsync(pending.AuthorizationUrl, cancellationToken)
            .ConfigureAwait(false);

        var authorizationCode = ReadAuthorizationCode(redirect, pending);

        var issued = await this
            .RedeemAsync(authorization, pending, listener.RedirectUri, authorizationCode, cancellationToken)
            .ConfigureAwait(false);

        this.tokens.Accept(issued);
    }

    /// <summary>Builds the address the person opens, and the proof the code will be redeemed with.</summary>
    internal static PendingSignIn BuildAuthorization(
        DeploymentAuthorization authorization,
        string clientId,
        Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var proofKey = PkceCodeChallenge.Create();
        var state = AntiForgeryState.Create();

        var query = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("response_type", "code"),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("state", state),
            new("code_challenge", proofKey.Challenge),
            new("code_challenge_method", "S256"),

            // RFC 8707. Without it a server protecting several resources issues a token whose audience is its own
            // default, and the deployment refuses it — correctly, and for a reason nothing in the refusal explains.
            new("resource", authorization.Resource),
        };

        // Verbatim from the deployment's metadata document. An empty scope parameter is not the same thing as an absent
        // one, and a deployment that requires none publishes an empty list, so nothing is sent rather than a blank.
        if (!string.IsNullOrWhiteSpace(authorization.Scope))
        {
            query.Add(new KeyValuePair<string, string>("scope", authorization.Scope));
        }

        return new PendingSignIn(
            new UriBuilder(authorization.AuthorizationEndpoint)
            {
                Query = Merge(authorization.AuthorizationEndpoint.Query, Encode(query)),
            }.Uri,
            state,
            proofKey);
    }

    /// <summary>Reads the code out of a redirect, once the redirect has been shown to belong to this sign-in.</summary>
    /// <remarks>
    /// The state is compared before anything else is read, including the error. A redirect that echoes something else
    /// was produced by an exchange this process did not start, so neither its code nor its refusal is this sign-in's to
    /// act on — and acting on the refusal would let anything that can navigate a browser end somebody's sign-in.
    /// </remarks>
    private static string ReadAuthorizationCode(SignInRedirect redirect, PendingSignIn pending)
    {
        if (!pending.MatchesReturnedState(redirect.State))
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The sign-in came back with an answer to a different request, so it was not completed.");
        }

        if (redirect.Error is { Length: > 0 })
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.CredentialRefused,
                "The sign-in was refused or dismissed. Try again.");
        }

        return redirect.Code is { Length: > 0 } code
            ? code
            : throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The sign-in came back without an authorization code, so there was nothing to exchange.");
    }

    /// <summary>Writes a query the way a form-encoded one is written.</summary>
    /// <remarks>
    /// Hand-rolled because <c>System.Web</c> is not part of a net10.0 library's framework, and because the alternative —
    /// reading a <see cref="FormUrlEncodedContent" /> back as a string — would make building an address an asynchronous
    /// operation for no gain.
    /// </remarks>
    /// <summary>Adds this request's parameters to whatever query the authorization endpoint already publishes.</summary>
    /// <remarks>
    /// RFC 6749 section 3.1: an authorization endpoint may carry a query component of its own — a tenant's policy
    /// parameter is the one that appears in practice — and it must be retained when parameters are added to it.
    /// Assigning <see cref="UriBuilder.Query" /> replaces the whole component, so the published one is read first and
    /// the request's parameters appended to it.
    /// </remarks>
    private static string Merge(string publishedQuery, string parameters)
    {
        var published = publishedQuery.TrimStart('?');

        return published.Length == 0 ? parameters : $"{published}&{parameters}";
    }

    private static string Encode(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join(
            '&',
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));

    /// <summary>Exchanges the authorization code the redirect carried for an access token.</summary>
    private async Task<string> RedeemAsync(
        DeploymentAuthorization authorization,
        PendingSignIn pending,
        Uri redirectUri,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", authorizationCode),
            new("client_id", this.options.ClientId),
            new("code_verifier", pending.ProofKey.Verifier),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("resource", authorization.Resource),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, authorization.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        using var response = await DeploymentExchange
            .SendAsync(this.authorizationServer, request, cancellationToken)
            .ConfigureAwait(false);

        // The status code is not the verdict. RFC 6749 requires a rejected grant to arrive as 400 with a
        // machine-readable 'error', so the body is read either way and the status consulted only when it holds nothing.
        var issued = await DeploymentExchange
            .ReadBodyAsync(response, DeploymentJsonContext.Default.OAuthTokenResponse, cancellationToken)
            .ConfigureAwait(false);

        if (issued.Error is { Length: > 0 })
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.CredentialRefused,
                "The authorization server did not accept the sign-in. Start it again.");
        }

        return issued.AccessToken is { Length: > 0 } accessToken
            ? accessToken
            : throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The authorization server answered without an access token, so there is nothing to present to MailFathom.");
    }
}
