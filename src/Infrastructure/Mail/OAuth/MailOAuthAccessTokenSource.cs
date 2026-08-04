// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Json;
using MailFathom.Application.Accounts;
using MailFathom.Application.Resilience;
using MailFathom.Common.OAuth;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Exchanges an account's configured grant for an access token at its authorization server.</summary>
/// <remarks>
/// <para>
/// This is the whole of MailFathom's OAuth client at run time, and it is deliberately RFC 6749 over
/// <see cref="HttpClient" /> rather than an identity library. Both supported grants are a form post to one endpoint
/// and a JSON response with three fields worth reading; a library would add a dependency, its own token cache
/// competing with the one here, and its own telemetry, in exchange for code this size.
/// </para>
/// <para>
/// The type is scoped, because resolving an account's settings reads the configuration snapshot the current scope
/// captured. The tokens themselves outlive any scope, so <see cref="MailAccessTokenCache" /> holds them as a
/// singleton and this type asks it for one; that split is what keeps a process-wide cache from ever capturing a
/// scoped dependency.
/// </para>
/// </remarks>
internal sealed class MailOAuthAccessTokenSource : IMailAccessTokenSource
{
    private readonly HttpClient httpClient;
    private readonly IMailOAuthSettingsProvider settingsProvider;
    private readonly IMailboxRefreshTokenStore refreshTokenStore;
    private readonly MailAccessTokenCache tokenCache;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MailOAuthAccessTokenSource> logger;

    /// <summary>Initializes a token source over a transport, the account settings, the stored grant, the shared cache, and the resilience budget.</summary>
    /// <param name="httpClient">The transport used for token requests.</param>
    /// <param name="settingsProvider">Resolves one account's endpoint and secrets per request.</param>
    /// <param name="refreshTokenStore">Holds the refresh token MailFathom stores, and receives the one a rotation issues.</param>
    /// <param name="tokenCache">Holds the issued tokens across scopes and serializes the requests that replace them.</param>
    /// <param name="operationExecutor">Applies the authorization-server resilience budget.</param>
    /// <param name="timeProvider">Supplies the instant an expiry is measured from.</param>
    /// <param name="logger">Records the outcome without recording any token.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailOAuthAccessTokenSource(
        HttpClient httpClient,
        IMailOAuthSettingsProvider settingsProvider,
        IMailboxRefreshTokenStore refreshTokenStore,
        MailAccessTokenCache tokenCache,
        OutboundOperationExecutor operationExecutor,
        TimeProvider timeProvider,
        ILogger<MailOAuthAccessTokenSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(refreshTokenStore);
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.settingsProvider = settingsProvider;
        this.refreshTokenStore = refreshTokenStore;
        this.tokenCache = tokenCache;
        this.operationExecutor = operationExecutor;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task<MailAccessToken> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken) =>
        this.tokenCache.GetOrIssueAsync(
            accountId,
            issueToken => this.RequestAccessTokenAsync(accountId, issueToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<MailAccessToken> RenewAccessTokenAsync(
        string accountId,
        MailAccessToken rejectedToken,
        CancellationToken cancellationToken) =>
        this.tokenCache.RenewAsync(
            accountId,
            rejectedToken,
            issueToken => this.RequestAccessTokenAsync(accountId, issueToken),
            cancellationToken);

    private async Task<MailAccessToken> RequestAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        var settings = await this.settingsProvider.GetSettingsAsync(accountId, cancellationToken);

        // The resolved material is owned by this request and released when it ends, whether it succeeded or not, so
        // the client secret exists for one request rather than for the lifetime of the process.
        using (settings.Material)
        {
            using var storedRefreshToken = settings.Grant.RequiresRefreshToken
                ? await this.refreshTokenStore.FindTokenAsync(
                    MailAccountId.Create(settings.AccountId),
                    cancellationToken)
                : null;

            var form = BuildTokenRequestForm(settings, storedRefreshToken);

            // Keyed per account, like the two mailbox classes are. Accounts do not share an authorization server —
            // one is at Google and the next at Entra — so a process-wide key would let one provider's outage open the
            // circuit for every other account's token requests, and would spend one concurrency budget across all of
            // them. The account identifier is MailFathom's own configured name, so it carries no personal data into
            // resilience telemetry.
            return await this.operationExecutor.ExecuteAsync(
                new OutboundPipelineKey(OutboundDependency.MailAuthorizationServerInvocation, settings.AccountId),
                operationKey: null,
                attemptToken => this.ExchangeGrantAsync(settings, form, attemptToken),
                cancellationToken);
        }
    }

    /// <summary>Builds the token request, preferring the stored refresh token over the configured reference.</summary>
    /// <remarks>
    /// The stored token wins because it is the newer of the two by construction: it exists only because the
    /// authorization server issued it in place of what was configured. The reference is the seed, so an account whose
    /// grant has never been stored — every deployment that predates this store, and every account authorized out of
    /// band — is served from it exactly as before, and stops being read from once the first rotation is stored.
    /// </remarks>
    private static Dictionary<string, string> BuildTokenRequestForm(
        MailOAuthAccountSettings settings,
        MailboxRefreshToken? storedRefreshToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = settings.Grant.GrantTypeName,
            ["client_id"] = settings.ClientId,
        };

        // A public client sends no secret at all, rather than an empty one: an empty `client_secret` is a value the
        // authorization server evaluates and rejects, while its absence is what identifies the client as public.
        if (settings.Material.ClientSecret is { } clientSecret)
        {
            form["client_secret"] = clientSecret.RevealAsString();
        }

        if (settings.Grant.RequiresRefreshToken)
        {
            // Startup validation already proved the account configures one, so the absence of both here would be a
            // defect rather than a configuration error to report.
            form["refresh_token"] = storedRefreshToken?.RevealAsString()
                ?? settings.Material.RefreshToken!.RevealAsString();
        }

        if (settings.Scope is { Length: > 0 })
        {
            form["scope"] = settings.Scope;
        }

        return form;
    }

    private async Task<MailAccessToken> ExchangeGrantAsync(
        MailOAuthAccountSettings settings,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        OAuthTokenResponse? payload;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await this.httpClient.PostAsync(settings.TokenEndpoint, content, cancellationToken);

            // The status code is not the verdict. RFC 6749 requires a rejected grant to arrive as 400 carrying a
            // machine-readable `error`, which says far more than the status does, so the body is read either way.
            payload = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(
                OAuthJsonContext.Default.Options,
                cancellationToken);
        }
        catch (Exception failure) when (failure is HttpRequestException or System.Text.Json.JsonException)
        {
            throw new MailAccessTokenUnavailableException(settings.AccountId, failure);
        }

        if (payload is null)
        {
            throw new MailAccessTokenUnavailableException(settings.AccountId, "empty_response");
        }

        if (payload.Error is { } error)
        {
            throw new MailAccessTokenUnavailableException(settings.AccountId, error);
        }

        if (payload.AccessToken is not { Length: > 0 } accessToken)
        {
            throw new MailAccessTokenUnavailableException(settings.AccountId, "no_access_token_issued");
        }

        await this.StoreRotatedRefreshTokenAsync(settings, payload, cancellationToken);

        // RFC 6749 leaves `expires_in` optional. An hour is what both supported providers issue, and treating an
        // unstated lifetime as very long would be the dangerous default: the token would be presented well past its
        // real expiry and the failure would surface as a mailbox authentication error.
        var lifetime = TimeSpan.FromSeconds(payload.ExpiresInSeconds ?? 3600);

        return new MailAccessToken(accessToken, this.timeProvider.GetUtcNow() + lifetime);
    }

    /// <summary>Stores the refresh token the authorization server issued in place of the one this request spent.</summary>
    /// <remarks>
    /// <para>
    /// Microsoft Entra issues a new refresh token on every refresh and invalidates the one it replaces, so following the
    /// rotation is what keeps an account working past the first refresh. A response carrying one under the
    /// client-credentials grant is ignored rather than stored: that grant sends no refresh token and a value it never
    /// asked for would be a credential nothing would ever spend.
    /// </para>
    /// <para>
    /// A failure to store is recorded and does not fail the request, which is deliberate and not merely lenient. The
    /// access token has already been issued, and this runs inside the resilience pipeline, so throwing would retry the
    /// whole exchange with the refresh token the server has just invalidated — turning a lost rotation into an account
    /// that cannot authenticate at all. The same window exists if the process stops between the response and the write:
    /// the rotation is lost and the account needs re-authorizing, which is the residual the store cannot close without
    /// the authorization server offering a two-phase acknowledgement it does not.
    /// </para>
    /// </remarks>
    private async Task StoreRotatedRefreshTokenAsync(
        MailOAuthAccountSettings settings,
        OAuthTokenResponse payload,
        CancellationToken cancellationToken)
    {
        if (!settings.Grant.RequiresRefreshToken || payload.RefreshToken is not { Length: > 0 } rotatedToken)
        {
            return;
        }

        using var refreshToken = MailboxRefreshToken.FromText(rotatedToken);

        try
        {
            await this.refreshTokenStore.SaveTokenAsync(
                MailAccountId.Create(settings.AccountId),
                refreshToken,
                cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            this.logger.LogError(
                failure,
                "The rotated refresh token the authorization server issued for account {AccountId} could not be stored. The account keeps working until the token it replaced stops being accepted, after which the authorization has to be run again.",
                settings.AccountId);
        }
    }
}
