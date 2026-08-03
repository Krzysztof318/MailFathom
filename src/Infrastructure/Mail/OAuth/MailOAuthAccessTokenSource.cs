// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Http.Json;
using MailFathom.Application.Resilience;
using MailFathom.Common.MailboxOAuth;
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
    private readonly MailAccessTokenCache tokenCache;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MailOAuthAccessTokenSource> logger;

    /// <summary>Initializes a token source over a transport, the account settings, the shared cache, and the resilience budget.</summary>
    /// <param name="httpClient">The transport used for token requests.</param>
    /// <param name="settingsProvider">Resolves one account's endpoint and secrets per request.</param>
    /// <param name="tokenCache">Holds the issued tokens across scopes and serializes the requests that replace them.</param>
    /// <param name="operationExecutor">Applies the authorization-server resilience budget.</param>
    /// <param name="timeProvider">Supplies the instant an expiry is measured from.</param>
    /// <param name="logger">Records the outcome without recording any token.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailOAuthAccessTokenSource(
        HttpClient httpClient,
        IMailOAuthSettingsProvider settingsProvider,
        MailAccessTokenCache tokenCache,
        OutboundOperationExecutor operationExecutor,
        TimeProvider timeProvider,
        ILogger<MailOAuthAccessTokenSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(tokenCache);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.settingsProvider = settingsProvider;
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
            var form = BuildTokenRequestForm(settings);

            return await this.operationExecutor.ExecuteAsync(
                OutboundDependency.MailAuthorizationServerInvocation,
                attemptToken => this.ExchangeGrantAsync(settings, form, attemptToken),
                cancellationToken);
        }
    }

    private static Dictionary<string, string> BuildTokenRequestForm(MailOAuthAccountSettings settings)
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
            // Startup validation already proved the account configures one, so its absence here would be a defect
            // rather than a configuration error to report.
            form["refresh_token"] = settings.Material.RefreshToken!.RevealAsString();
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
        MailOAuthTokenResponse? payload;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await this.httpClient.PostAsync(settings.TokenEndpoint, content, cancellationToken);

            // The status code is not the verdict. RFC 6749 requires a rejected grant to arrive as 400 carrying a
            // machine-readable `error`, which says far more than the status does, so the body is read either way.
            payload = await response.Content.ReadFromJsonAsync<MailOAuthTokenResponse>(
                MailOAuthJsonContext.Default.Options,
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

        this.WarnWhenRefreshTokenRotated(settings, payload);

        // RFC 6749 leaves `expires_in` optional. An hour is what both supported providers issue, and treating an
        // unstated lifetime as very long would be the dangerous default: the token would be presented well past its
        // real expiry and the failure would surface as a mailbox authentication error.
        var lifetime = TimeSpan.FromSeconds(payload.ExpiresInSeconds ?? 3600);

        return new MailAccessToken(accessToken, this.timeProvider.GetUtcNow() + lifetime);
    }

    /// <summary>Records that the authorization server rotated the refresh token, which this deployment cannot follow.</summary>
    /// <remarks>
    /// Microsoft Entra issues a new refresh token on every refresh. MailFathom reads its refresh token from a secret
    /// reference it has no write access to — deliberately, because a process that could rewrite its own credentials
    /// could also destroy them — so the configured token keeps being used until an operator replaces it. That works
    /// while the previous token stays valid, and it is the operator who has to know the clock is running, which is
    /// why this is a warning rather than a silent discard.
    /// </remarks>
    private void WarnWhenRefreshTokenRotated(MailOAuthAccountSettings settings, MailOAuthTokenResponse payload)
    {
        if (payload.RefreshToken is { Length: > 0 })
        {
            this.logger.LogWarning(
                "The authorization server issued a rotated refresh token for account {AccountId}, which the configured secret reference cannot receive. Re-run the authorization before the configured token stops being accepted.",
                settings.AccountId);
        }
    }
}
