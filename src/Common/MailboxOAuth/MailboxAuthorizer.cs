// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Web;
using MailFathom.Common.OAuth;

namespace MailFathom.Common.MailboxOAuth;

/// <summary>Runs the one-time, operator-driven exchange that produces a mailbox refresh token.</summary>
/// <remarks>
/// <para>
/// This is deliberately not part of the running service. The host is headless, ships in a container, and authenticates
/// with a refresh token it was given; obtaining that token needs a person to sign in, which is an administration act
/// with its own lifetime. Keeping it in a separate executable is what lets the host serve no consent page, own no
/// redirect endpoint, and hold no authorization-server credential it does not need at run time.
/// </para>
/// <para>
/// Two grants reach a person, and which one is available is the provider's decision rather than a preference:
/// Microsoft Entra issues device codes for the IMAP scopes, while Google's device flow is restricted to a scope list
/// that contains no mail scope at all, so a Google mailbox has to go through the authorization-code grant. Both paths
/// end at the same token endpoint and both are bound by PKCE where the grant allows it.
/// </para>
/// </remarks>
public sealed class MailboxAuthorizer
{
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes an authorizer over a transport and a clock.</summary>
    /// <param name="httpClient">The transport used for authorization-server requests.</param>
    /// <param name="timeProvider">Supplies the current instant and the polling delay.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailboxAuthorizer(HttpClient httpClient, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.httpClient = httpClient;
        this.timeProvider = timeProvider;
    }

    /// <summary>Builds the address a person opens to authorize the mailbox, and the proof the code is redeemed with.</summary>
    /// <param name="request">The provider endpoints and registered client.</param>
    /// <returns>The pending authorization to show the operator, which <see cref="RedeemAuthorizationCodeAsync" /> later redeems.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the request carries no authorization endpoint or redirect address.</exception>
    public PendingAuthorization BuildAuthorization(MailboxAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AuthorizationEndpoint is not { } authorizationEndpoint || request.RedirectUri is not { } redirectUri)
        {
            throw new InvalidOperationException("The authorization-code grant needs both an authorization endpoint and a redirect address.");
        }

        var proofKey = PkceCodeChallenge.Create();
        var state = AntiForgeryState.Create();

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = request.ClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = redirectUri.ToString();
        query["scope"] = request.Scope;
        query["state"] = state;
        query["code_challenge"] = proofKey.Challenge;
        query["code_challenge_method"] = "S256";

        // Google issues a refresh token only when consent is forced and offline access is asked for by name. Microsoft
        // reads offline access from the scope list instead, and ignores both of these.
        query["access_type"] = "offline";
        query["prompt"] = "consent";

        var authorizationUrl = new UriBuilder(authorizationEndpoint) { Query = query.ToString() }.Uri;

        return new PendingAuthorization(authorizationUrl, state, proofKey);
    }

    /// <summary>Exchanges an authorization code the operator pasted back for a refresh token.</summary>
    /// <param name="request">The provider endpoints and registered client.</param>
    /// <param name="pendingAuthorization">The authorization <see cref="BuildAuthorization" /> produced, carrying the proof key the code is bound to.</param>
    /// <param name="authorizationCode">The code the redirect carried.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The grant to provision.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="MailboxAuthorizationFailedException">Thrown when the authorization server refused the exchange.</exception>
    public async Task<MailboxAuthorizationGrant> RedeemAuthorizationCodeAsync(
        MailboxAuthorizationRequest request,
        PendingAuthorization pendingAuthorization,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pendingAuthorization);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["client_id"] = request.ClientId,
            ["code_verifier"] = pendingAuthorization.ProofKey.Verifier,
        };

        if (request.RedirectUri is { } redirectUri)
        {
            form["redirect_uri"] = redirectUri.ToString();
        }

        if (request.ClientSecret is { Length: > 0 } clientSecret)
        {
            form["client_secret"] = clientSecret;
        }

        var response = await this.PostFormAsync(
            request.TokenEndpoint,
            form,
            OAuthJsonContext.Default.OAuthTokenResponse,
            cancellationToken);

        return this.ToGrant(response);
    }

    /// <summary>Requests a device code, reports the prompt, and polls until the person completes the sign-in.</summary>
    /// <param name="request">The provider endpoints and registered client.</param>
    /// <param name="reportPrompt">Receives the code and address to show the person, once, before polling begins. Invoked on the calling thread, so whatever it writes has been written by the time the wait starts.</param>
    /// <param name="cancellationToken">Cancels the request and the polling.</param>
    /// <returns>The grant to provision.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the request names no device authorization endpoint.</exception>
    /// <exception cref="MailboxAuthorizationFailedException">Thrown when the authorization server refused, or the code expired unredeemed.</exception>
    /// <remarks>
    /// Polling honors the interval the authorization server stated and the <c>slow_down</c> answer RFC 8628 defines,
    /// because a client that polls faster than it was told is throttled or blocked outright.
    /// </remarks>
    public async Task<MailboxAuthorizationGrant> AuthorizeWithDeviceCodeAsync(
        MailboxAuthorizationRequest request,
        Action<DeviceCodePrompt> reportPrompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reportPrompt);

        if (request.DeviceAuthorizationEndpoint is not { } deviceAuthorizationEndpoint)
        {
            throw new InvalidOperationException("The device-code grant needs a device authorization endpoint, and this provider offers none.");
        }

        var deviceAuthorization = await this.RequestDeviceAuthorizationAsync(
            deviceAuthorizationEndpoint,
            request,
            cancellationToken);

        reportPrompt(DescribePrompt(deviceAuthorization, this.timeProvider.GetUtcNow()));

        return await this.PollForDeviceGrantAsync(request, deviceAuthorization, cancellationToken);
    }

    private async Task<OAuthDeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(
        Uri deviceAuthorizationEndpoint,
        MailboxAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = request.ClientId,
            ["scope"] = request.Scope,
        };

        var response = await this.PostFormAsync(
            deviceAuthorizationEndpoint,
            form,
            OAuthJsonContext.Default.OAuthDeviceAuthorizationResponse,
            cancellationToken);

        if (response.Error is { } error)
        {
            throw new MailboxAuthorizationFailedException(error);
        }

        return response.DeviceCode is null || response.UserCode is null || response.VerificationUri is null
            ? throw new MailboxAuthorizationFailedException("device_authorization_incomplete")
            : response;
    }

    private async Task<MailboxAuthorizationGrant> PollForDeviceGrantAsync(
        MailboxAuthorizationRequest request,
        OAuthDeviceAuthorizationResponse deviceAuthorization,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = request.ClientId,
            ["device_code"] = deviceAuthorization.DeviceCode!,
        };

        if (request.ClientSecret is { Length: > 0 } clientSecret)
        {
            form["client_secret"] = clientSecret;
        }

        var pollInterval = deviceAuthorization.IntervalSeconds is { } interval and > 0
            ? TimeSpan.FromSeconds(interval)
            : DeviceCodePolling.DefaultInterval;

        var expiresAt = this.timeProvider.GetUtcNow()
            + TimeSpan.FromSeconds(deviceAuthorization.ExpiresInSeconds ?? 600);

        while (this.timeProvider.GetUtcNow() < expiresAt)
        {
            await Task.Delay(pollInterval, this.timeProvider, cancellationToken);

            var response = await this.PostFormAsync(
                request.TokenEndpoint,
                form,
                OAuthJsonContext.Default.OAuthTokenResponse,
                cancellationToken);

            switch (response.Error)
            {
                case null:
                    return this.ToGrant(response);

                // The person has not finished signing in. This is the expected answer for most of the wait.
                case "authorization_pending":
                    continue;

                // The authorization server is telling this client it polls too fast, and the RFC requires the interval
                // to grow permanently rather than for one iteration.
                case "slow_down":
                    pollInterval += DeviceCodePolling.BackoffIncrement;
                    continue;

                default:
                    throw new MailboxAuthorizationFailedException(response.Error);
            }
        }

        throw new MailboxAuthorizationFailedException("expired_token");
    }

    private static DeviceCodePrompt DescribePrompt(OAuthDeviceAuthorizationResponse deviceAuthorization, DateTimeOffset asOf) =>
        new(
            deviceAuthorization.UserCode!,
            new Uri(deviceAuthorization.VerificationUri!),
            deviceAuthorization.VerificationUriComplete is { } completeUri ? new Uri(completeUri) : null,
            asOf + TimeSpan.FromSeconds(deviceAuthorization.ExpiresInSeconds ?? 600));

    private MailboxAuthorizationGrant ToGrant(OAuthTokenResponse response)
    {
        if (response.Error is { } error)
        {
            throw new MailboxAuthorizationFailedException(error);
        }

        // A grant without a refresh token authenticates once and then strands the deployment, so it is refused here
        // rather than provisioned and discovered when the first access token expires. Google withholds one unless
        // offline access was requested and consent was forced; Microsoft withholds one unless `offline_access` is in
        // the scope list.
        return response.RefreshToken is { Length: > 0 } refreshToken
            ? new MailboxAuthorizationGrant(
                refreshToken,
                this.timeProvider.GetUtcNow() + TimeSpan.FromSeconds(response.ExpiresInSeconds ?? 3600))
            : throw new MailboxAuthorizationFailedException("no_refresh_token_issued");
    }

    private async Task<TResponse> PostFormAsync<TResponse>(
        Uri endpoint,
        Dictionary<string, string> form,
        JsonTypeInfo<TResponse> responseContract,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await this.httpClient.PostAsync(endpoint, content, cancellationToken);

        TResponse? payload;
        try
        {
            // The status code is not the verdict. RFC 6749 requires a rejected grant to arrive as 400 with a machine
            // readable `error`, so the body is read either way and the status is consulted only when it holds nothing.
            payload = await response.Content.ReadFromJsonAsync(responseContract, cancellationToken);
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException or InvalidOperationException)
        {
            // A mistyped endpoint reaches a login page, a proxy, or an error page rather than a token endpoint, and an
            // operator running this by hand should be told which status came back instead of meeting a stack trace.
            // The body itself is not read: it is attacker-influenced text from a machine that is not the one intended.
            // An answer naming a character set this platform does not carry never reaches the parser at all, and
            // arrives from the transport as an InvalidOperationException; it is the same defect and reads the same way.
            throw new MailboxAuthorizationFailedException(
                string.Create(CultureInfo.InvariantCulture, $"non_json_response_http_{(int)response.StatusCode}"));
        }

        return payload ?? throw new MailboxAuthorizationFailedException(
            string.Create(CultureInfo.InvariantCulture, $"http_{(int)response.StatusCode}"));
    }
}
