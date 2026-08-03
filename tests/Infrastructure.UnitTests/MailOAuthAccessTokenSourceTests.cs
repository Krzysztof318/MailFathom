// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Secrets;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers the token exchange itself: what goes into the form, and what each answer is turned into.</summary>
/// <remarks>
/// This is the only production code that builds an RFC 6749 token request, and every rule in it is one an authorization
/// server enforces silently — a `client_secret` sent as an empty string is rejected where its absence identifies a
/// public client, and a missing `expires_in` decides when the token is replaced. Reaching it through a mailbox session
/// would assert those through a substitute that never sees the form.
/// </remarks>
public sealed class MailOAuthAccessTokenSourceTests
{
    private const string Account = "primary";

    /// <summary>A public client sends no field at all; an empty one is a value the server evaluates and refuses.</summary>
    [Fact]
    public async Task GetAccessTokenAsync_PublicClient_SendsNoClientSecretFieldRatherThanAnEmptyOne()
    {
        // Arrange
        using var context = TokenEndpointAnswering(SuccessfulTokenResponse("an-access-token", expiresInSeconds: 3600));

        // Act
        await context.Source.GetAccessTokenAsync(Account, CancellationToken.None);

        // Assert
        var form = ReadForm(context.Handler);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("a-refresh-token", form["refresh_token"]);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ConfidentialClient_SendsTheResolvedClientSecret()
    {
        // Arrange
        using var context = TokenEndpointAnswering(
            SuccessfulTokenResponse("an-access-token", expiresInSeconds: 3600),
            clientSecret: "a-client-secret");

        // Act
        await context.Source.GetAccessTokenAsync(Account, CancellationToken.None);

        // Assert
        var form = ReadForm(context.Handler);
        Assert.Equal("a-client-secret", form["client_secret"]);
    }

    /// <summary>
    /// An authorization server may state no lifetime, and a token treated as immortal is one presented after the mail
    /// server has stopped accepting it. The hour the code assumes is what bounds that.
    /// </summary>
    [Fact]
    public async Task GetAccessTokenAsync_ResponseStatingNoLifetime_ExpiresTheTokenAnHourOn()
    {
        // Arrange
        using var context = TokenEndpointAnswering("""{"access_token":"an-access-token"}""");

        // Act
        var token = await context.Source.GetAccessTokenAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(context.Host.TimeProvider.GetUtcNow().AddSeconds(3600), token.ExpiresAt);
    }

    /// <summary>The error code reaches an operator through a message, so it is sanitized rather than copied.</summary>
    [Fact]
    public async Task GetAccessTokenAsync_ServerRefusingTheGrant_ReportsTheSanitizedErrorCode()
    {
        // Arrange
        using var context = TokenEndpointAnswering(
            """{"error":"invalid_grant\nforged log line"}""",
            status: HttpStatusCode.BadRequest);

        // Act
        var failure = await Assert.ThrowsAsync<MailAccessTokenUnavailableException>(
            () => context.Source.GetAccessTokenAsync(Account, CancellationToken.None));

        // Assert
        Assert.Equal("invalid_grantforgedlogline", failure.AuthorizationServerErrorCode);
        Assert.DoesNotContain('\n', failure.Message);
    }

    /// <summary>A body that is not a token response is a refusal, not an empty success handed to the mail server.</summary>
    [Fact]
    public async Task GetAccessTokenAsync_AnswerCarryingNoAccessToken_IsRefusedRatherThanIssued()
    {
        // Arrange
        using var context = TokenEndpointAnswering("""{"token_type":"Bearer"}""");

        // Act, Assert
        await Assert.ThrowsAsync<MailAccessTokenUnavailableException>(
            () => context.Source.GetAccessTokenAsync(Account, CancellationToken.None));
    }

    private static string SuccessfulTokenResponse(string accessToken, int expiresInSeconds) =>
        $$"""{"access_token":"{{accessToken}}","expires_in":{{expiresInSeconds}}}""";

    private static Dictionary<string, string> ReadForm(FakeHttpMessageHandler handler)
    {
        var request = Assert.Single(handler.RecordedRequests);

        return request
            .ContentAsUtf8String()
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty,
                StringComparer.Ordinal);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each object is handed to the returned context, whose Dispose releases all of them; disposing them here would return a context of closed resources.")]
    private static TokenEndpointContext TokenEndpointAnswering(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        string? clientSecret = null)
    {
        var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        var host = OutboundResilienceTestHost.WithConfiguredSettings();
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var cache = new MailAccessTokenCache(host.TimeProvider);

        var source = new MailOAuthAccessTokenSource(
            httpClient,
            new FakeMailOAuthSettingsProvider(clientSecret),
            cache,
            host.Executor,
            host.TimeProvider,
            host.Services.GetRequiredService<ILogger<MailOAuthAccessTokenSource>>());

        return new TokenEndpointContext(host, handler, httpClient, cache, source);
    }

    private sealed record TokenEndpointContext(
        OutboundResilienceTestHost Host,
        FakeHttpMessageHandler Handler,
        HttpClient HttpClient,
        MailAccessTokenCache Cache,
        MailOAuthAccessTokenSource Source) : IDisposable
    {
        public void Dispose()
        {
            this.HttpClient.Dispose();
            this.Cache.Dispose();
            this.Handler.Dispose();
            this.Host.Dispose();
        }
    }

    /// <summary>Supplies settings for one account, resolving its material the way the configured provider does.</summary>
    /// <remarks>
    /// The material is deliberately not disposed here. The port's contract is that the operation which requested the
    /// settings owns it and releases it when the request ends, which is what keeps a live client secret from outliving
    /// one token request, and a fake that disposed it would be testing a different contract.
    /// </remarks>
    private sealed class FakeMailOAuthSettingsProvider(string? clientSecret) : IMailOAuthSettingsProvider
    {
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The material is handed to the caller, which owns it and disposes it when the token request ends; that transfer is the port's stated contract.")]
        public Task<MailOAuthAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken)
        {
            var material = new MailOAuthClientMaterial(
                clientSecret is null ? null : ResolvedSecret.FromText(clientSecret),
                ResolvedSecret.FromText("a-refresh-token"));

            return Task.FromResult(new MailOAuthAccountSettings(
                accountId,
                new Uri("https://oauth2.example.test/token"),
                "a-client-id",
                "https://mail.example.test/",
                MailOAuthGrant.RefreshToken,
                material));
        }
    }
}
