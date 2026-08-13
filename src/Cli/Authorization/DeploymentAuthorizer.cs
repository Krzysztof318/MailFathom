// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Web;
using MailFathom.Cli.Commands;
using MailFathom.Common.OAuth;

namespace MailFathom.Cli.Authorization;

/// <summary>Signs an operator in to one deployment, and keeps that sign-in usable until it genuinely ends.</summary>
/// <remarks>
/// <para>
/// The command is a public client: it ships as a binary an operator downloads, so it holds no client secret and could
/// keep none. Every grant is therefore bound by PKCE, and the authorization request names the deployment's resource so
/// the issued token's audience is the endpoint being signed in to rather than anything else that server protects.
/// </para>
/// <para>
/// Two grants reach a person, and which to use is the operator's answer about their machine rather than the provider's.
/// The authorization-code grant catches the redirect on a loopback address here and completes with nothing to copy; the
/// device-code grant of RFC 8628 needs no browser on this machine at all, which is what a jump host has instead.
/// </para>
/// <para>
/// A rotated refresh token is deliberately not adopted; see <see cref="RefreshAsync" /> for what that means and why.
/// </para>
/// </remarks>
internal sealed class DeploymentAuthorizer
{
    /// <summary>The interval RFC 8628 mandates when the device authorization response states none.</summary>
    private static readonly TimeSpan DefaultDevicePollInterval = TimeSpan.FromSeconds(5);

    /// <summary>The extra wait RFC 8628 requires after the authorization server answers <c>slow_down</c>.</summary>
    private static readonly TimeSpan DevicePollBackoffIncrement = TimeSpan.FromSeconds(5);

    /// <summary>How long a device code stays pollable when the authorization server states no lifetime.</summary>
    private static readonly TimeSpan DefaultDeviceCodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The access-token lifetime assumed when the authorization server states none.</summary>
    /// <remarks>Conservative on purpose: assuming too little costs one refresh nobody notices, while assuming too much would present a spent token to the deployment and surface as a command failing.</remarks>
    private static readonly TimeSpan AssumedAccessTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long before expiry an access token is treated as spent.</summary>
    /// <remarks>
    /// A token that expires while the request carrying it is in flight is refused, and the operator would read that as
    /// a sign-in that failed for no reason. Renewing a minute early costs one exchange and removes the race; the same
    /// skew the service applies to its own mailbox tokens, for the same reason.
    /// </remarks>
    internal static readonly TimeSpan RenewalSkew = TimeSpan.FromMinutes(1);

    private readonly HttpClient transport;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes an authorizer over a transport and a clock.</summary>
    /// <param name="transport">The transport used for authorization-server requests.</param>
    /// <param name="timeProvider">Supplies the current instant and the polling delay.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DeploymentAuthorizer(HttpClient transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.transport = transport;
        this.timeProvider = timeProvider;
    }

    /// <summary>Builds the address a person opens to approve the sign-in, and the proof the code is redeemed with.</summary>
    /// <param name="authorization">Where to authorize, and for what.</param>
    /// <param name="clientId">The client identifier registered with the authorization server.</param>
    /// <param name="redirectUri">The loopback address the redirect comes back to.</param>
    /// <returns>The pending authorization the redirect is matched against.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the authorization server publishes no authorization endpoint.</exception>
    internal static PendingSignIn BuildAuthorization(
        DeploymentAuthorization authorization,
        string clientId,
        Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(redirectUri);

        if (authorization.AuthorizationEndpoint is not { } authorizationEndpoint)
        {
            throw new CliFailure(
                $"The authorization server at {authorization.Issuer} publishes no authorization endpoint, so a browser sign-in is not possible there. Try --mode device.");
        }

        var proofKey = PkceCodeChallenge.Create();
        var state = CreateAntiForgeryState();

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = clientId;
        query["response_type"] = "code";
        query["redirect_uri"] = redirectUri.ToString();
        query["state"] = state;
        query["code_challenge"] = proofKey.Challenge;
        query["code_challenge_method"] = "S256";

        // RFC 8707. Without it a server protecting several resources issues a token whose audience is its own default,
        // and the deployment refuses it — correctly, and for a reason nothing in the refusal explains.
        query["resource"] = authorization.Resource;

        // Verbatim from the deployment's metadata document, including 'offline_access' where it advertises one. Adding a
        // scope the document never named would ask an authorization server for something the operator did not intend,
        // and a session that outlives the first hour is the deployment's decision to publish rather than this client's
        // to assume.
        if (authorization.Scope is { Length: > 0 })
        {
            query["scope"] = authorization.Scope;
        }

        return new PendingSignIn(
            new UriBuilder(authorizationEndpoint) { Query = query.ToString() }.Uri,
            state,
            proofKey);
    }

    /// <summary>Exchanges the authorization code the redirect carried for a session.</summary>
    /// <param name="authorization">Where to authorize, and for what.</param>
    /// <param name="clientId">The client identifier registered with the authorization server.</param>
    /// <param name="pendingSignIn">The authorization <see cref="BuildAuthorization" /> produced, carrying the proof key the code is bound to.</param>
    /// <param name="redirectUri">The loopback address the redirect came back to, which the server compares against the one it was given.</param>
    /// <param name="authorizationCode">The code the redirect carried.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The session to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the authorization server refused the exchange.</exception>
    internal Task<DeploymentGrant> RedeemAuthorizationCodeAsync(
        DeploymentAuthorization authorization,
        string clientId,
        PendingSignIn pendingSignIn,
        Uri redirectUri,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(pendingSignIn);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);

        return this.ExchangeAsync(
            authorization,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["client_id"] = clientId,
                ["code_verifier"] = pendingSignIn.ProofKey.Verifier,
                ["redirect_uri"] = redirectUri.ToString(),
                ["resource"] = authorization.Resource,
            },
            GrantAttempt.AuthorizationCode,
            cancellationToken);
    }

    /// <summary>Requests a device code, reports the prompt, and polls until the person completes the sign-in.</summary>
    /// <param name="authorization">Where to authorize, and for what.</param>
    /// <param name="clientId">The client identifier registered with the authorization server.</param>
    /// <param name="reportPrompt">Receives the code and address to show the person, once, before polling begins. Invoked on the calling thread, so whatever it writes has been written by the time the wait starts.</param>
    /// <param name="cancellationToken">Cancels the request and the polling.</param>
    /// <returns>The session to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the server offers no device grant, refused it, or the code expired unredeemed.</exception>
    /// <remarks>Polling honors the interval the authorization server stated and the <c>slow_down</c> answer RFC 8628 defines, because a client that polls faster than it was told is throttled or blocked outright.</remarks>
    internal async Task<DeploymentGrant> AuthorizeWithDeviceCodeAsync(
        DeploymentAuthorization authorization,
        string clientId,
        Action<DeviceCodePrompt> reportPrompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(reportPrompt);

        if (authorization.DeviceAuthorizationEndpoint is not { } deviceAuthorizationEndpoint)
        {
            throw new CliFailure(
                $"The authorization server at {authorization.Issuer} publishes no device authorization endpoint, so a device sign-in is not possible there. Use the interactive mode on a machine with a browser.");
        }

        var deviceAuthorization = await this.RequestDeviceAuthorizationAsync(
            deviceAuthorizationEndpoint,
            authorization,
            clientId,
            cancellationToken);

        reportPrompt(DescribePrompt(deviceAuthorization, this.timeProvider.GetUtcNow()));

        return await this.PollForDeviceGrantAsync(authorization, clientId, deviceAuthorization, cancellationToken);
    }

    /// <summary>Exchanges a refresh token for a fresh access token, leaving the session's end where it was.</summary>
    /// <param name="authorization">Where the exchange happens, and for what.</param>
    /// <param name="clientId">The client identifier registered with the authorization server.</param>
    /// <param name="refreshToken">The stored refresh token.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>The renewed session, carrying the refresh token that was presented rather than any the server returned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the authorization server refused the refresh token, which is what a session ending looks like.</exception>
    /// <remarks>
    /// <para>
    /// <strong>A rotated refresh token is not adopted.</strong> An authorization server that answers a refresh with a
    /// new refresh token is offering to extend the session, and taking that offer would mean an operator's sign-in
    /// lasted as long as they kept using it — with revocation at the authorization server effective only whenever they
    /// happened to stop. Keeping the token that was issued at sign-in gives the session a definite end that nothing
    /// here can move.
    /// </para>
    /// <para>
    /// The cost is real and belongs to the operator rather than to a later reader: a server that invalidates the old
    /// token when it rotates ends the session at the next renewal instead of at the refresh token's own expiry. That
    /// arrives as a refusal naming what happened and what to run, never as an unexplained failure.
    /// </para>
    /// <para>
    /// The service's own mailbox credentials take the opposite decision and store the rotation, which is not an
    /// inconsistency: a synchronizing account is headless and must keep reading a mailbox with nobody there to sign it
    /// in, while this session belongs to a person who is present and can sign in again in seconds.
    /// </para>
    /// </remarks>
    internal async Task<DeploymentGrant> RefreshAsync(
        DeploymentAuthorization authorization,
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(refreshToken);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["resource"] = authorization.Resource,
        };

        AddPublishedScopes(form, authorization);

        var renewed = await this.ExchangeAsync(
            authorization,
            form,
            GrantAttempt.StoredRefreshToken,
            cancellationToken);

        return renewed with { RefreshToken = refreshToken };
    }

    /// <summary>Asks for the scopes the deployment published, and asks for nothing where it published none.</summary>
    /// <remarks>An empty <c>scope</c> parameter is not the same thing as an absent one, and a deployment that requires and advertises no scope publishes an empty list — so the parameter is left out rather than sent empty to a server that may well refuse it.</remarks>
    private static void AddPublishedScopes(Dictionary<string, string> form, DeploymentAuthorization authorization)
    {
        if (authorization.Scope is { Length: > 0 })
        {
            form["scope"] = authorization.Scope;
        }
    }

    /// <summary>Creates the opaque value the redirect must echo, which is what binds a returned code to this request.</summary>
    private static string CreateAntiForgeryState() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static DeviceCodePrompt DescribePrompt(
        OAuthDeviceAuthorizationResponse deviceAuthorization,
        DateTimeOffset asOf) =>
        new(
            deviceAuthorization.UserCode!,
            ReadVerificationAddress(deviceAuthorization.VerificationUri!),
            deviceAuthorization.VerificationUriComplete is { } completeUri
                ? ReadVerificationAddress(completeUri)
                : null,
            asOf + LifetimeOf(deviceAuthorization.ExpiresInSeconds, DefaultDeviceCodeLifetime));

    /// <summary>Reads an address the authorization server published for a person to open.</summary>
    /// <remarks>
    /// Parsed rather than constructed, because the value is JSON from a machine this process does not own and
    /// <see cref="Uri(string)" /> answers a malformed one with an exception nothing here translates — which reaches the
    /// operator as a stack trace where every other malformed answer reaches them as a sentence. The scheme is checked
    /// as well: this address is printed for a person to open, so one that is not web-addressable is not something to
    /// put in front of them, however well formed it parses.
    /// </remarks>
    private static Uri ReadVerificationAddress(string published) =>
        Uri.TryCreate(published, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? address
            : throw new CliFailure(
                "The authorization server answered a device sign-in with a verification address that is not a usable web address.");

    private static TimeSpan LifetimeOf(int? statedSeconds, TimeSpan whenUnstated) =>
        statedSeconds is { } seconds and > 0 ? TimeSpan.FromSeconds(seconds) : whenUnstated;

    /// <summary>Turns an authorization server's own error code into a message that says what to do about it.</summary>
    /// <remarks>
    /// <para>
    /// The code is sanitized before it is read, because it is text from a machine this process does not own and a raw
    /// one could carry line breaks that forge a second line of output.
    /// </para>
    /// <para>
    /// <c>invalid_grant</c> and <c>expired_token</c> are separated out because they are the ordinary end of something
    /// rather than a fault — but which thing ended depends on what was presented, which is why the attempt is a
    /// parameter. The same code means a replayed or expired authorization code during a sign-in, a device code that
    /// outlived the person's attention, and a refresh token that expired or was revoked; naming the refresh token in
    /// all three would tell an operator signing in for the first time that a token they have never had is no longer
    /// accepted.
    /// </para>
    /// </remarks>
    private static CliFailure DescribeRefusal(string errorCode, GrantAttempt attempt)
    {
        var code = AuthorizationServerErrorText.Sanitize(errorCode);

        if (code is not ("invalid_grant" or "expired_token"))
        {
            return new CliFailure($"The authorization server refused the request ('{code}').");
        }

        return attempt switch
        {
            GrantAttempt.AuthorizationCode => new CliFailure(
                $"The authorization server did not accept the code the redirect carried ('{code}'). Run the command again to start a new sign-in."),
            GrantAttempt.DeviceCode => new CliFailure(
                $"The device code is no longer valid ('{code}'). Run the command again to start a new sign-in."),
            _ => new CliFailure(
                $"The sign-in has ended: the authorization server no longer accepts the stored refresh token ('{code}'). Run '{CliRootCommand.CommandName} login --endpoint <address>' to sign in again."),
        };
    }

    private async Task<OAuthDeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(
        Uri deviceAuthorizationEndpoint,
        DeploymentAuthorization authorization,
        string clientId,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["resource"] = authorization.Resource,
        };

        AddPublishedScopes(form, authorization);

        var response = await this.PostFormAsync(
            deviceAuthorizationEndpoint,
            form,
            CliJsonContext.Default.OAuthDeviceAuthorizationResponse,
            cancellationToken);

        if (response.Error is { } error)
        {
            throw DescribeRefusal(error, GrantAttempt.DeviceCode);
        }

        return response.DeviceCode is null || response.UserCode is null || response.VerificationUri is null
            ? throw new CliFailure("The authorization server answered a device sign-in without the code or the address a person needs.")
            : response;
    }

    private async Task<DeploymentGrant> PollForDeviceGrantAsync(
        DeploymentAuthorization authorization,
        string clientId,
        OAuthDeviceAuthorizationResponse deviceAuthorization,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = clientId,
            ["device_code"] = deviceAuthorization.DeviceCode!,
            ["resource"] = authorization.Resource,
        };

        var pollInterval = LifetimeOf(deviceAuthorization.IntervalSeconds, DefaultDevicePollInterval);
        var expiresAt = this.timeProvider.GetUtcNow()
            + LifetimeOf(deviceAuthorization.ExpiresInSeconds, DefaultDeviceCodeLifetime);

        while (this.timeProvider.GetUtcNow() < expiresAt)
        {
            await Task.Delay(pollInterval, this.timeProvider, cancellationToken);

            var response = await this.PostFormAsync(
                authorization.TokenEndpoint,
                form,
                CliJsonContext.Default.OAuthTokenResponse,
                cancellationToken);

            switch (response.Error)
            {
                case null:
                    return this.ToGrant(response, GrantAttempt.DeviceCode);

                // The person has not finished signing in. This is the expected answer for most of the wait.
                case "authorization_pending":
                    continue;

                // The server is telling this client it polls too fast, and RFC 8628 requires the interval to grow
                // permanently rather than for one iteration.
                case "slow_down":
                    pollInterval += DevicePollBackoffIncrement;
                    continue;

                default:
                    throw DescribeRefusal(response.Error, GrantAttempt.DeviceCode);
            }
        }

        throw new CliFailure("The device code expired before the sign-in was completed. Run the command again.");
    }

    private async Task<DeploymentGrant> ExchangeAsync(
        DeploymentAuthorization authorization,
        Dictionary<string, string> form,
        GrantAttempt attempt,
        CancellationToken cancellationToken)
    {
        var response = await this.PostFormAsync(
            authorization.TokenEndpoint,
            form,
            CliJsonContext.Default.OAuthTokenResponse,
            cancellationToken);

        return this.ToGrant(response, attempt);
    }

    /// <summary>Reads an issued grant, or turns what the server said instead into a failure naming what was presented.</summary>
    /// <remarks>Whether a refresh token is required follows from the attempt rather than being stated beside it: a sign-in that produces none leaves a session ending within the hour, while a renewal is the one exchange that is not expected to return one.</remarks>
    private DeploymentGrant ToGrant(OAuthTokenResponse response, GrantAttempt attempt)
    {
        if (response.Error is { } error)
        {
            throw DescribeRefusal(error, attempt);
        }

        if (response.AccessToken is not { Length: > 0 } accessToken)
        {
            throw new CliFailure("The authorization server answered without an access token, so there is nothing to present to the deployment.");
        }

        // A session with no refresh token is one that ends when the first access token does, which the operator would
        // meet as a command failing an hour after signing in. Refused where it is issued rather than stored and
        // discovered then; a server withholds one when offline access was not granted.
        if (attempt is not GrantAttempt.StoredRefreshToken && response.RefreshToken is not { Length: > 0 })
        {
            throw new CliFailure(
                "The authorization server issued no refresh token, so the sign-in would end within the hour. Grant the client offline access at the authorization server and have the deployment advertise 'offline_access', or sign in with an API key instead.");
        }

        return new DeploymentGrant(
            accessToken,
            response.RefreshToken ?? string.Empty,
            this.timeProvider.GetUtcNow() + LifetimeOf(response.ExpiresInSeconds, AssumedAccessTokenLifetime));
    }

    private async Task<TResponse> PostFormAsync<TResponse>(
        Uri endpoint,
        Dictionary<string, string> form,
        JsonTypeInfo<TResponse> responseContract,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var content = new FormUrlEncodedContent(form);

        try
        {
            using var response = await this.transport.PostAsync(endpoint, content, cancellationToken);

            TResponse? payload;

            try
            {
                // The status code is not the verdict. RFC 6749 requires a rejected grant to arrive as 400 with a
                // machine-readable 'error', so the body is read either way and the status consulted only when the body
                // holds nothing.
                payload = await response.Content.ReadFromJsonAsync(responseContract, cancellationToken);
            }
            catch (Exception failure) when (failure is JsonException or NotSupportedException)
            {
                // A mistyped or hijacked endpoint reaches a login page, a proxy, or an error page rather than a token
                // endpoint. The body itself is never read back: it is attacker-influenced text from a machine that is
                // not the one intended.
                throw new CliFailure(
                    string.Create(CultureInfo.InvariantCulture, $"The authorization server answered {(int)response.StatusCode} with something that is not a token response."),
                    failure);
            }

            return payload ?? throw new CliFailure(
                string.Create(CultureInfo.InvariantCulture, $"The authorization server answered {(int)response.StatusCode} with an empty body."));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CliFailure($"The authorization server at {endpoint.GetLeftPart(UriPartial.Authority)} did not answer in time.");
        }
        catch (HttpRequestException failure)
        {
            // The message is the transport's rather than the server's, so it carries no credential.
            throw new CliFailure(
                $"The authorization server at {endpoint.GetLeftPart(UriPartial.Authority)} could not be reached: {failure.Message}",
                failure);
        }
    }

    /// <summary>What was presented to the token endpoint, which is what an <c>invalid_grant</c> is about.</summary>
    private enum GrantAttempt
    {
        /// <summary>The code a redirect carried, redeemed once at the end of an interactive sign-in.</summary>
        AuthorizationCode = 0,

        /// <summary>The device code, polled until the person finishes at the verification address.</summary>
        DeviceCode = 1,

        /// <summary>The stored refresh token, presented to renew an access token that is about to expire.</summary>
        StoredRefreshToken = 2,
    }
}
