// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using System.Web;
using MailFathom.Common.MailboxOAuth;
using MailFathom.Common.OAuth;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Common.UnitTests;

public sealed class MailboxAuthorizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri TokenEndpoint = new("https://authorization.test/token");
    private static readonly Uri DeviceEndpoint = new("https://authorization.test/devicecode");
    private static readonly Uri AuthorizationEndpoint = new("https://authorization.test/authorize");
    private static readonly Uri RedirectUri = new("http://localhost:8765/");

    [Fact]
    public void BuildAuthorization_AuthorizationCodeRequest_CarriesProofKeyAndForcesOfflineConsent()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));

        // Act
        var pending = authorizer.BuildAuthorization(CreateRequest());

        // Assert
        var query = HttpUtility.ParseQueryString(pending.AuthorizationUrl.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.Equal(pending.ExpectedState, query["state"]);
        // Google issues no refresh token without both of these, and a grant without one strands the deployment.
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
    }

    [Fact]
    public void BuildAuthorization_TwoRuns_ProduceDifferentStateAndProofKeys()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));

        // Act
        var first = authorizer.BuildAuthorization(CreateRequest());
        var second = authorizer.BuildAuthorization(CreateRequest());

        // Assert
        Assert.NotEqual(first.ExpectedState, second.ExpectedState);
        Assert.NotEqual(
            HttpUtility.ParseQueryString(first.AuthorizationUrl.Query)["code_challenge"],
            HttpUtility.ParseQueryString(second.AuthorizationUrl.Query)["code_challenge"]);
    }

    [Fact]
    public async Task RedeemAuthorizationCodeAsync_SuccessfulExchange_SendsTheProofKeyAndReturnsTheRefreshToken()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));

            Assert.Equal("authorization_code", form["grant_type"]);
            Assert.Equal("returned-code", form["code"]);
            Assert.False(string.IsNullOrWhiteSpace(form["code_verifier"]));

            return JsonResponse("""{"access_token":"at","refresh_token":"rt","expires_in":3600}""");
        });
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest();
        var pending = authorizer.BuildAuthorization(request);

        // Act
        var grant = await authorizer.RedeemAuthorizationCodeAsync(
            request,
            pending,
            "returned-code",
            CancellationToken.None);

        // Assert
        Assert.Equal("rt", grant.RefreshToken);
        Assert.Equal(Now.AddHours(1), grant.AccessTokenExpiresAt);
    }

    /// <summary>
    /// The body is read through the source-generated contract rather than through serializer options, because the
    /// options overload resolves the shape by reflection and the trimmed command cannot carry that. Case-insensitive
    /// matching is the part of the contract the two ways of writing the read do not obviously share, and a server whose
    /// casing differs from the specification's must not authorize a mailbox here and fail at <c>login</c>.
    /// </summary>
    [Fact]
    public async Task RedeemAuthorizationCodeAsync_AServerVaryingTheCaseOfTheTokenResponse_ReadsTheGrantTheSameWay()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse("""{"Access_Token":"at","Refresh_Token":"rt","Expires_In":3600}""")));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest();
        var pending = authorizer.BuildAuthorization(request);

        // Act
        var grant = await authorizer.RedeemAuthorizationCodeAsync(request, pending, "code", CancellationToken.None);

        // Assert
        Assert.Equal("rt", grant.RefreshToken);
        Assert.Equal(Now.AddHours(1), grant.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task RedeemAuthorizationCodeAsync_ResponseWithoutRefreshToken_FailsRatherThanProvisioningADeadGrant()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse("""{"access_token":"at","expires_in":3600}""")));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest();
        var pending = authorizer.BuildAuthorization(request);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxAuthorizationFailedException>(
            () => authorizer.RedeemAuthorizationCodeAsync(request, pending, "code", CancellationToken.None));

        // Assert
        Assert.Equal("no_refresh_token_issued", failure.AuthorizationServerErrorCode);
    }

    [Fact]
    public async Task RedeemAuthorizationCodeAsync_RejectedGrant_ReportsTheAuthorizationServersOwnErrorCode()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse("""{"error":"invalid_grant","error_description":"code expired"}""", HttpStatusCode.BadRequest)));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest();
        var pending = authorizer.BuildAuthorization(request);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxAuthorizationFailedException>(
            () => authorizer.RedeemAuthorizationCodeAsync(request, pending, "code", CancellationToken.None));

        // Assert
        Assert.Equal("invalid_grant", failure.AuthorizationServerErrorCode);
        // The free-text description may echo the rejected request, so it must never reach an operator-visible message.
        Assert.DoesNotContain("code expired", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizeWithDeviceCodeAsync_PendingThenApproved_ReportsThePromptAndPollsUntilTheGrantArrives()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        var tokenRequestCount = 0;
        using var transport = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri == DeviceEndpoint)
            {
                return Task.FromResult(JsonResponse(
                    """{"device_code":"dc","user_code":"ABCD-EFGH","verification_uri":"https://authorization.test/activate","expires_in":600,"interval":5}"""));
            }

            tokenRequestCount++;

            return Task.FromResult(tokenRequestCount < 3
                ? JsonResponse("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest)
                : JsonResponse("""{"access_token":"at","refresh_token":"rt","expires_in":3600}"""));
        });
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, timeProvider);

        DeviceCodePrompt? reportedPrompt = null;
        var pollsBeforeThePromptWasReported = -1;

        // Act
        var authorization = authorizer.AuthorizeWithDeviceCodeAsync(
            CreateRequest(),
            prompt =>
            {
                reportedPrompt = prompt;
                pollsBeforeThePromptWasReported = tokenRequestCount;
            },
            CancellationToken.None);

        await AdvanceUntilCompletedAsync(timeProvider, authorization);
        var grant = await authorization;

        // Assert
        Assert.Equal("rt", grant.RefreshToken);
        Assert.Equal(3, tokenRequestCount);
        Assert.Equal("ABCD-EFGH", reportedPrompt?.UserCode);
        Assert.Equal(new Uri("https://authorization.test/activate"), reportedPrompt?.VerificationUri);

        // The person cannot act on a code they have not been shown, so the prompt reaches them before the first poll
        // rather than whenever a queued callback happens to run.
        Assert.Equal(0, pollsBeforeThePromptWasReported);
    }

    [Fact]
    public async Task AuthorizeWithDeviceCodeAsync_SlowDown_LengthensTheIntervalForEveryLaterPoll()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        var pollInstants = new List<DateTimeOffset>();
        using var transport = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri == DeviceEndpoint)
            {
                return Task.FromResult(JsonResponse(
                    """{"device_code":"dc","user_code":"CODE","verification_uri":"https://authorization.test/activate","expires_in":600,"interval":5}"""));
            }

            pollInstants.Add(timeProvider.GetUtcNow());

            return Task.FromResult(pollInstants.Count switch
            {
                1 => JsonResponse("""{"error":"slow_down"}""", HttpStatusCode.BadRequest),
                2 => JsonResponse("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest),
                _ => JsonResponse("""{"access_token":"at","refresh_token":"rt","expires_in":3600}"""),
            });
        });
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, timeProvider);

        // Act
        var authorization = authorizer.AuthorizeWithDeviceCodeAsync(
            CreateRequest(),
            _ => { },
            CancellationToken.None);

        await AdvanceUntilCompletedAsync(timeProvider, authorization);
        await authorization;

        // Assert: the first wait is the stated 5 seconds, and every wait after slow_down is 10.
        var waits = pollInstants
            .Zip(pollInstants.Skip(1), (earlier, later) => later - earlier)
            .ToArray();

        Assert.Equal(Now.AddSeconds(5), pollInstants[0]);
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)], waits);
    }

    [Fact]
    public async Task AuthorizeWithDeviceCodeAsync_ProviderWithoutADeviceEndpoint_RefusesBeforeAnyRequest()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("No request should be sent."));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest() with { DeviceAuthorizationEndpoint = null };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => authorizer.AuthorizeWithDeviceCodeAsync(
            request,
            _ => { },
            CancellationToken.None));
    }

    [Fact]
    public void ToString_RequestAndGrant_AreRedactedBecauseBothCarryCredentials()
    {
        // Arrange
        var request = CreateRequest();
        var grant = new MailboxAuthorizationGrant("a-refresh-token", Now);

        // Act, Assert
        Assert.Equal("***", request.ToString());
        Assert.Equal("***", grant.ToString());
        Assert.DoesNotContain("a-refresh-token", grant.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A mistyped endpoint reaches a login page, a proxy, or an error page rather than a token endpoint. That has to
    /// arrive as an authorization failure the command reports, not as an unhandled parse exception and a stack trace
    /// in an operator's terminal.
    /// </summary>
    [Fact]
    public async Task RedeemAuthorizationCodeAsync_NonJsonResponse_FailsAsAnAuthorizationFailureNamingTheStatus()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<html><body>Not found</body></html>", Encoding.UTF8, "text/html"),
        }));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest();
        var pending = authorizer.BuildAuthorization(request);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxAuthorizationFailedException>(
            () => authorizer.RedeemAuthorizationCodeAsync(request, pending, "code", CancellationToken.None));

        // Assert: the status is named, and the body — attacker-influenced text — is not.
        Assert.Equal("non_json_response_http_404", failure.AuthorizationServerErrorCode);
        Assert.DoesNotContain("Not found", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizeWithDeviceCodeAsync_NonJsonResponse_FailsAsAnAuthorizationFailure()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream unavailable", Encoding.UTF8, "text/plain"),
        }));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));

        // Act, Assert
        var failure = await Assert.ThrowsAsync<MailboxAuthorizationFailedException>(
            () => authorizer.AuthorizeWithDeviceCodeAsync(
                CreateRequest(),
                _ => { },
                CancellationToken.None));

        Assert.Equal("non_json_response_http_502", failure.AuthorizationServerErrorCode);
    }

    /// <summary>
    /// The device grant reaches the token endpoint from inside the polling loop, which is a second place a proxy or an
    /// error page answers instead of the authorization server. It is mapped there for the reason it is mapped on the
    /// first request: an operator running this by hand meets a sentence rather than a stack trace.
    /// </summary>
    [Fact]
    public async Task AuthorizeWithDeviceCodeAsync_ATokenEndpointAnsweringSomethingThatIsNotJson_FailsAsAnAuthorizationFailure()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Now);
        using var transport = new FakeHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri == DeviceEndpoint
                ? JsonResponse(
                    """{"device_code":"dc","user_code":"CODE","verification_uri":"https://authorization.test/activate","expires_in":600,"interval":5}""")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("<html><body>maintenance</body></html>", Encoding.UTF8, "text/html"),
                }));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, timeProvider);

        // Act
        var authorization = authorizer.AuthorizeWithDeviceCodeAsync(CreateRequest(), _ => { }, CancellationToken.None);
        await AdvanceUntilCompletedAsync(timeProvider, authorization);

        // Assert: the status is named, and the body — attacker-influenced text — is not.
        var failure = await Assert.ThrowsAsync<MailboxAuthorizationFailedException>(() => authorization);
        Assert.Equal("non_json_response_http_503", failure.AuthorizationServerErrorCode);
        Assert.DoesNotContain("maintenance", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A token endpoint naming a character set this platform does not carry is the other way a well-formed HTTP answer
    /// is not a token response, and it arrives before a byte of the body is parsed. Left unmapped it escapes the
    /// exchange as the transport's own exception, which is the stack trace every other malformed answer avoids.
    /// </summary>
    [Fact]
    public async Task RedeemAuthorizationCodeAsync_AnAnswerInAnUnsupportedCharacterSet_FailsAsAnAuthorizationFailure()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse("""{"access_token":"at","refresh_token":"rt","expires_in":3600}""", characterSet: "iso-8859-2")));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var request = CreateRequest();
        var pending = authorizer.BuildAuthorization(request);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxAuthorizationFailedException>(
            () => authorizer.RedeemAuthorizationCodeAsync(request, pending, "code", CancellationToken.None));

        // Assert
        Assert.Equal("non_json_response_http_200", failure.AuthorizationServerErrorCode);
    }

    [Fact]
    public void MatchesReturnedState_TheValueTheAuthorizationWasIssuedWith_IsAccepted()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var pending = authorizer.BuildAuthorization(CreateRequest());

        // Act, Assert: surrounding whitespace survives because the value arrives by copy and paste.
        Assert.True(pending.MatchesReturnedState(pending.ExpectedState));
        Assert.True(pending.MatchesReturnedState($"  {pending.ExpectedState}  "));
    }

    /// <summary>
    /// This is the anti-forgery check on the manual flow. A code that arrived with a state this process never issued
    /// came from a different authorization, and redeeming it would bind the operator's mailbox to somebody else's
    /// grant.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0000000000000000")]
    public void MatchesReturnedState_AValueThisAuthorizationDidNotIssue_IsRefused(string? returnedState)
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var pending = authorizer.BuildAuthorization(CreateRequest());

        // Act, Assert
        Assert.False(pending.MatchesReturnedState(returnedState));
    }

    [Fact]
    public void MatchesReturnedState_TheStateOfADifferentAuthorization_IsRefused()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var first = authorizer.BuildAuthorization(CreateRequest());
        var second = authorizer.BuildAuthorization(CreateRequest());

        // Act, Assert
        Assert.False(first.MatchesReturnedState(second.ExpectedState));
    }

    [Fact]
    public void MatchesReturnedState_TheExpectedValueInADifferentCase_IsRefused()
    {
        // Arrange
        using var transport = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
        using var httpClient = new HttpClient(transport);
        var authorizer = new MailboxAuthorizer(httpClient, new FakeTimeProvider(Now));
        var pending = authorizer.BuildAuthorization(CreateRequest());

        // Arrange: the state is upper-case hexadecimal, so folding each character is what changes the value. Folded
        // per character rather than through string.ToLowerInvariant, which CA1308 rejects.
        var caseFolded = new string([.. pending.ExpectedState.Select(char.ToLowerInvariant)]);

        // Act, Assert: the comparison is ordinal, so a case-folded echo is not the value that was issued.
        Assert.False(pending.MatchesReturnedState(caseFolded));
    }

    /// <summary>Advances virtual time in polling-sized steps until the authorization settles.</summary>
    /// <remarks>
    /// The device grant waits on <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)" />, which only
    /// completes when the fake clock moves. Advancing in a loop rather than by one large jump keeps each poll a
    /// separate observable instant, which is what the interval assertions read.
    /// </remarks>
    private static async Task AdvanceUntilCompletedAsync(FakeTimeProvider timeProvider, Task authorization)
    {
        for (var step = 0; step < 100 && !authorization.IsCompleted; step++)
        {
            // Yields so the continuation behind the delay runs before the clock moves again.
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(5));
        }
    }

    private static MailboxAuthorizationRequest CreateRequest() => new(
        AuthorizationEndpoint,
        TokenEndpoint,
        DeviceEndpoint,
        "client-id",
        "client-secret",
        "https://mail.example.test/scope",
        RedirectUri);

    private static HttpResponseMessage JsonResponse(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? characterSet = null)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        if (characterSet is not null)
        {
            content.Headers.ContentType!.CharSet = characterSet;
        }

        return new HttpResponseMessage(statusCode) { Content = content };
    }
}
