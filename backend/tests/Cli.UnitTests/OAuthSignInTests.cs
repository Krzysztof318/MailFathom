// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using System.Web;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers signing in with OAuth, and what keeps that sign-in usable afterwards.</summary>
/// <remarks>
/// The claims worth defending are that nothing but the client identifier is configured — every other value comes from
/// the deployment or the server it names — and that the session has a definite end nothing here can move.
/// </remarks>
public sealed class OAuthSignInTests : IDisposable
{
    private const string ClientId = "mfctl";

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-oauth-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Login_AnInteractiveSignIn_StoresTheIssuedSessionAndItsExpiry()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(0, exitCode);

        var profile = store.Resolve(requestedDeployment: null);
        Assert.Equal("an-access-token", profile.Token);
        Assert.Equal("a-refresh-token", profile.Session?.RefreshToken);
        Assert.Equal(this.clock.GetUtcNow().AddSeconds(3600), profile.Session?.AccessTokenExpiresAt);
        Assert.Equal(FakeOAuthDeployment.Issuer, profile.Session?.Issuer);
    }

    /// <summary>
    /// The audience check on the other side is what stops a token issued for another service being replayed at this one,
    /// and it only passes when the client asked for this resource by name. Without RFC 8707's parameter the deployment
    /// refuses a token that is otherwise perfectly valid, for a reason nothing in the refusal explains.
    /// </summary>
    [Fact]
    public async Task Login_AnInteractiveSignIn_AsksForATokenBoundToTheDeploymentsResource()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();

        // Act
        await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        Assert.Equal(FakeOAuthDeployment.Resource, deployment.LastTokenRequest["resource"]);
        Assert.Equal("authorization_code", deployment.LastTokenRequest["grant_type"]);
        Assert.Equal(ClientId, deployment.LastTokenRequest["client_id"]);
    }

    /// <summary>PKCE is what makes a code intercepted on a shared machine useless, and the command is a public client with nothing else to prove itself by.</summary>
    [Fact]
    public async Task Login_AnInteractiveSignIn_BindsTheCodeToAProofKey()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();

        // Act
        await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        var authorizationQuery = HttpUtility.ParseQueryString(new Uri(this.AuthorizationAddress()).Query);
        Assert.Equal("S256", authorizationQuery["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(authorizationQuery["code_challenge"]));
        Assert.False(string.IsNullOrWhiteSpace(deployment.LastTokenRequest["code_verifier"]));
    }

    /// <summary>The scopes come from the deployment, so an operator never has to know what this resource requires.</summary>
    [Fact]
    public async Task Login_AnInteractiveSignIn_AsksForTheScopesTheDeploymentPublishes()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishedScopes = ["mailfathom.admin", "mailfathom.read", "offline_access"];
        using var handler = deployment.Handler();

        // Act
        await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        var requested = HttpUtility.ParseQueryString(new Uri(this.AuthorizationAddress()).Query)["scope"]!.Split(' ');
        Assert.Equal(["mailfathom.admin", "mailfathom.read", "offline_access"], requested);
    }

    /// <summary>
    /// The document states what to ask for, and this command asks for that and nothing more. It used to append
    /// <c>offline_access</c> itself, which meant a deployment could not decide whether its clients hold a refresh token;
    /// now the deployment advertises the scope and every client reading the same document asks for it, rather than only
    /// the one client that hard-coded the value.
    /// </summary>
    [Fact]
    public async Task Login_ADeploymentAdvertisingNoOfflineAccess_AsksForNothingItDidNotPublish()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishedScopes = ["mailfathom.admin"];
        using var handler = deployment.Handler();

        // Act
        await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        var requested = HttpUtility.ParseQueryString(new Uri(this.AuthorizationAddress()).Query)["scope"]!.Split(' ');
        Assert.Equal(["mailfathom.admin"], requested);
    }

    /// <summary>
    /// A deployment requiring and advertising nothing publishes an empty list, and an empty <c>scope</c> parameter is
    /// not the same request as one carrying none — several authorization servers refuse the first outright. Nothing is
    /// substituted for the absence either: the sign-in then ends within the hour, which is the deployment's own choice.
    /// </summary>
    [Fact]
    public async Task Login_ADeploymentPublishingNoScopeAtAll_AsksWithNoScopeParameter()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishedScopes = [];
        using var handler = deployment.Handler();

        // Act
        await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        Assert.Null(HttpUtility.ParseQueryString(new Uri(this.AuthorizationAddress()).Query)["scope"]);
    }

    /// <summary>A redirect echoing a value this run never issued belongs to a different request, so nothing may be redeemed against it.</summary>
    [Fact]
    public async Task Login_ARedirectEchoingAnotherRunsState_RedeemsNothingAndStoresNothing()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await RunAsync(
            this.Context(store, handler, FakeMailboxRedirect.Approving("a-code", "a-state-this-run-never-issued")),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "interactive",
            "--client-id",
            ClientId);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.DoesNotContain("authorization_code", deployment.LastTokenRequest.Values);
    }

    /// <summary>
    /// The same token response reaches this process through two serialization contexts, and the mailbox one has always
    /// read it without regard to case. A server whose casing differs from the specification's must not sign in through
    /// one command and fail through the other.
    /// </summary>
    [Fact]
    public async Task Login_AServerVaryingTheCaseOfTheTokenResponse_SignsInTheSameWayMailboxAuthorizationDoes()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.AnswerTokenRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"Access_Token":"an-access-token","token_type":"Bearer","Expires_In":3600,"Refresh_Token":"a-refresh-token"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.NotNull(Assert.Single(store.Read().Profiles).Value.Session);
    }

    /// <summary>A person who declines at the authorization server is told what happened, rather than left watching a listener that has already been answered.</summary>
    [Fact]
    public async Task Login_ARedirectCarryingTheServersRefusal_ReportsItAndStoresNothing()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await RunAsync(
            this.Context(
                store,
                handler,
                FakeMailboxRedirect.Answering(new MailboxRedirect(Code: null, State: null, Error: "access_denied"))),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "interactive",
            "--client-id",
            ClientId);

        // Assert: the refusal is reported before the anti-forgery check, which a refused redirect carries nothing for.
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("refused the sign-in", StringComparison.Ordinal)
                && line.Contains("access_denied", StringComparison.Ordinal));
        Assert.Empty(deployment.LastTokenRequest);
    }

    /// <summary>A redirect carrying neither a code nor an error has nothing to redeem, and saying so beats posting an empty grant.</summary>
    [Fact]
    public async Task Login_ARedirectCarryingNoCodeAndNoError_ReportsItAndStoresNothing()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await RunAsync(
            this.Context(
                store,
                handler,
                FakeMailboxRedirect.EchoingStateWithoutACode(this.StateTheCommandGenerated)),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "interactive",
            "--client-id",
            ClientId);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("no authorization code", StringComparison.Ordinal));
        Assert.Empty(deployment.LastTokenRequest);
    }

    /// <summary>A session with no refresh token ends within the hour, which the operator would meet as a command failing rather than as a sign-in that never worked.</summary>
    [Fact]
    public async Task Login_AServerThatIssuesNoRefreshToken_FailsRatherThanStoringASessionThatEndsWithinTheHour()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.AnswerTokenRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                FakeOAuthDeployment.TokenResponse("an-access-token", refreshToken: null, expiresInSeconds: 3600),
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(this.console.Errors, line => line.Contains("no refresh token", StringComparison.Ordinal));
    }

    /// <summary>
    /// An operator signing in for the first time has no stored refresh token, so a refusal naming one describes a
    /// state they were never in and sends them looking for something that does not exist.
    /// </summary>
    [Theory]
    [InlineData("invalid_grant")]
    [InlineData("expired_token")]
    public async Task Login_AnInteractiveSignInTheServerRefuses_BlamesTheCodeRatherThanAStoredRefreshToken(
        string errorCode)
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.AnswerTokenRequest = _ => FakeOAuthDeployment.Refusing(errorCode);

        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("did not accept the code", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.console.Errors,
            line => line.Contains("stored refresh token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_ADeviceSignIn_PrintsTheCodeAndStoresTheIssuedSession()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // The device grant polls, and the poll waits on the injected clock rather than on a real delay.
        var signIn = RunAsync(
            this.Context(store, handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Act
        await this.AdvanceUntilCompleteAsync(signIn);

        // Assert
        Assert.Equal(0, await signIn);
        Assert.Contains(this.console.Errors, line => line.Contains("WDJB-MJHT", StringComparison.Ordinal));
        Assert.Equal("an-access-token", store.Resolve(requestedDeployment: null).Token);
        Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", deployment.LastTokenRequest["grant_type"]);
    }

    /// <summary>
    /// A device sign-in asks for its scopes at the device authorization endpoint, before any code exists to exchange,
    /// so that request is the only place a wrong scope list would appear. Dropping a scope the deployment advertises
    /// costs the person a refresh token they were meant to have and shows up as nothing else.
    /// </summary>
    [Fact]
    public async Task Login_ADeviceSignIn_AsksTheDeviceEndpointForTheScopesTheDeploymentPublishes()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishedScopes = ["mailfathom.admin", "offline_access"];
        using var handler = deployment.Handler();

        // The device grant polls, and the poll waits on the injected clock rather than on a real delay.
        var signIn = RunAsync(
            this.Context(this.CreateStore(), handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Act
        await this.AdvanceUntilCompleteAsync(signIn);

        // Assert
        Assert.Equal(0, await signIn);
        Assert.Equal("mailfathom.admin offline_access", deployment.LastDeviceAuthorizationRequest["scope"]);
    }

    /// <summary>The same guard as the interactive request, on the request that carries it: an empty <c>scope</c> parameter is not an absent one.</summary>
    [Fact]
    public async Task Login_ADeviceSignInAgainstADeploymentPublishingNoScope_AsksWithNoScopeParameter()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishedScopes = [];
        using var handler = deployment.Handler();

        // The device grant polls, and the poll waits on the injected clock rather than on a real delay.
        var signIn = RunAsync(
            this.Context(this.CreateStore(), handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Act
        await this.AdvanceUntilCompleteAsync(signIn);

        // Assert
        Assert.Equal(0, await signIn);
        Assert.DoesNotContain("scope", deployment.LastDeviceAuthorizationRequest.Keys);
    }

    /// <summary>A document answering with blanks composes into a scope parameter made of spaces, which is the empty one the guard exists to avoid — and it comes from a machine this process does not own.</summary>
    [Fact]
    public async Task Login_ADeploymentPublishingBlankScopes_AsksWithNoScopeParameter()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishedScopes = [" ", string.Empty];
        using var handler = deployment.Handler();

        // Act
        await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        Assert.Null(HttpUtility.ParseQueryString(new Uri(this.AuthorizationAddress()).Query)["scope"]);
    }

    /// <summary>A server publishing no device endpoint is reported as that, rather than as a sign-in that hangs on a grant it will never answer.</summary>
    [Fact]
    public async Task Login_ADeviceSignInAtAServerOfferingNone_SaysSoRatherThanPolling()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.OffersDeviceGrant = false;
        using var handler = deployment.Handler();

        // Act
        var exitCode = await RunAsync(
            this.Context(this.CreateStore(), handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("no device authorization endpoint", StringComparison.Ordinal));
    }

    /// <summary>A device code that outlived the person's attention is its own end, and naming a stored refresh token for it describes something a first sign-in never had.</summary>
    [Fact]
    public async Task Login_ADeviceSignInTheServerRefuses_BlamesTheDeviceCodeRatherThanAStoredRefreshToken()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.AnswerTokenRequest = _ => FakeOAuthDeployment.Refusing("expired_token");

        using var handler = deployment.Handler();

        var signIn = RunAsync(
            this.Context(this.CreateStore(), handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Act
        await this.AdvanceUntilCompleteAsync(signIn);

        // Assert
        Assert.Equal(1, await signIn);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("device code is no longer valid", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.console.Errors,
            line => line.Contains("stored refresh token", StringComparison.Ordinal));
    }

    /// <summary>
    /// The verification address is read verbatim out of a response this process does not own. Constructing a
    /// <see cref="Uri" /> from a malformed one throws where nothing translates it, so the operator would meet a stack
    /// trace where every other malformed answer reaches them as a sentence.
    /// </summary>
    [Theory]
    [InlineData("not a url at all")]
    [InlineData("/device")]
    [InlineData("javascript:alert(1)")]
    public async Task Login_ADeviceSignInWhoseVerificationAddressIsUnusable_ReportsItRatherThanCrashing(string published)
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.VerificationUri = published;

        using var handler = deployment.Handler();

        var signIn = RunAsync(
            this.Context(this.CreateStore(), handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Act
        await this.AdvanceUntilCompleteAsync(signIn);

        // Assert
        Assert.Equal(1, await signIn);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("not a usable web address", StringComparison.Ordinal));
    }

    /// <summary>The one-hour access-token lifetime must not be something the operator experiences.</summary>
    [Fact]
    public async Task Status_AnExpiredAccessToken_IsRenewedSilentlyAndWrittenBack()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        await this.RunInteractiveAsync(store, handler);

        deployment.AnswerTokenRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                FakeOAuthDeployment.TokenResponse("a-renewed-access-token", "a-rotated-refresh-token", 3600),
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        this.clock.Advance(TimeSpan.FromHours(1));

        // Act
        var exitCode = await RunAsync(
            this.Context(store, handler, FakeMailboxRedirect.Silent()),
            "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("refresh_token", deployment.LastTokenRequest["grant_type"]);
        Assert.Equal("a-renewed-access-token", store.Resolve(requestedDeployment: null).Token);

        // The renewal asks for the same scopes the sign-in did, so a session does not quietly narrow as it is renewed.
        Assert.Equal("mailfathom.admin", deployment.LastTokenRequest["scope"]);
    }

    /// <summary>
    /// A rotated refresh token is deliberately not adopted, so the operator's session has an end nothing in the command
    /// can move. Adopting one would make the session last as long as it was used, and revoking access at the
    /// authorization server would take effect only whenever the operator happened to stop.
    /// </summary>
    [Fact]
    public async Task Status_AServerThatRotatesTheRefreshToken_KeepsPresentingTheOneIssuedAtSignIn()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        await this.RunInteractiveAsync(store, handler);

        deployment.AnswerTokenRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                FakeOAuthDeployment.TokenResponse("a-renewed-access-token", "a-rotated-refresh-token", 3600),
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        // Act
        this.clock.Advance(TimeSpan.FromHours(1));
        await RunAsync(this.Context(store, handler, FakeMailboxRedirect.Silent()), "status");

        this.clock.Advance(TimeSpan.FromHours(1));
        await RunAsync(this.Context(store, handler, FakeMailboxRedirect.Silent()), "status");

        // Assert
        Assert.Equal(["a-refresh-token", "a-refresh-token"], deployment.PresentedRefreshTokens);
        Assert.Equal("a-refresh-token", store.Resolve(requestedDeployment: null).Session?.RefreshToken);
    }

    /// <summary>
    /// The end of a session has to read as an end. A server that invalidates a rotated token, a revoked grant, and an
    /// expired refresh token all arrive as the same refusal, and it must send the operator to <c>login</c> rather than
    /// to the service's logs.
    /// </summary>
    [Theory]
    [InlineData("invalid_grant")]
    [InlineData("expired_token")]
    public async Task Status_ARefreshTokenTheServerNoLongerAccepts_SaysTheSignInEndedAndWhatToRun(string errorCode)
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        await this.RunInteractiveAsync(store, handler);

        deployment.AnswerTokenRequest = _ => FakeOAuthDeployment.Refusing(errorCode);
        this.clock.Advance(TimeSpan.FromHours(1));

        // Act
        var exitCode = await RunAsync(this.Context(store, handler, FakeMailboxRedirect.Silent()), "status");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("sign-in has ended", StringComparison.Ordinal));
        Assert.Contains(this.console.Errors, line => line.Contains("login", StringComparison.Ordinal));
    }

    /// <summary>A token still inside its lifetime must not provoke an exchange, or every command would cost a round trip to the authorization server.</summary>
    [Fact]
    public async Task Status_AnAccessTokenThatHasNotExpired_ReachesTheDeploymentWithoutRenewingAnything()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        await this.RunInteractiveAsync(store, handler);

        this.clock.Advance(TimeSpan.FromMinutes(10));

        // Act
        var exitCode = await RunAsync(this.Context(store, handler, FakeMailboxRedirect.Silent()), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(deployment.PresentedRefreshTokens);
    }

    /// <summary>Which population an operator belongs to is not something the command can work out, so it asks rather than taking the first.</summary>
    [Fact]
    public async Task Login_ADeploymentAcceptingSeveralAuthorizationServers_AsksWhichOneRatherThanChoosing()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.Issuers = [FakeOAuthDeployment.Issuer, "https://partner.example.test/realms/partners"];
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(this.console.Errors, line => line.Contains("--issuer", StringComparison.Ordinal));
    }

    /// <summary>
    /// A token endpoint naming a character set this platform does not carry answers something that is not a token
    /// response, and it does so before a byte of the body is parsed. Left unmapped it reaches the operator as the
    /// transport's own exception rather than as the sentence every other malformed answer produces.
    /// </summary>
    [Fact]
    public async Task Login_ATokenEndpointAnsweringInAnUnsupportedCharacterSet_IsReportedRatherThanThrown()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.AnswerTokenRequest = _ =>
        {
            var content = new StringContent(
                FakeOAuthDeployment.TokenResponse("an-access-token", "a-refresh-token", expiresInSeconds: 3600),
                Encoding.UTF8,
                "application/json");

            content.Headers.ContentType!.CharSet = "iso-8859-2";

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(this.console.Errors, line => line.Contains("not a token response", StringComparison.Ordinal));
    }

    /// <summary>An endpoint that accepts only API keys publishes no metadata, and the refusal has to name the way in that does work.</summary>
    [Fact]
    public async Task Login_ADeploymentPublishingNoOAuthMetadata_SaysToUseAnApiKeyInstead()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Answering(HttpStatusCode.NotFound);

        // Act
        var exitCode = await this.RunInteractiveAsync(this.CreateStore(), handler);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("API key", StringComparison.Ordinal));
    }

    /// <summary>
    /// Keycloak, Entra ID, and Auth0 all publish OpenID Connect Discovery, and several publish nothing at the RFC 8414
    /// address at all. The candidate order exists for exactly that, and without this test a fault in it would leave
    /// sign-in broken against most real authorization servers with nothing failing here.
    /// </summary>
    [Fact]
    public async Task Login_AServerPublishingOnlyOpenIdConnectDiscovery_IsFoundAtTheNextCandidateAddress()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        deployment.PublishesOAuthMetadataAddress = false;
        using var handler = deployment.Handler();
        var store = this.CreateStore();

        // Act
        var exitCode = await this.RunInteractiveAsync(store, handler);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("an-access-token", store.Resolve(requestedDeployment: null).Token);
    }

    /// <summary>
    /// RFC 8628 requires the interval to grow permanently once a server answers <c>slow_down</c>, and a client that
    /// keeps polling at the old rate is throttled or blocked outright. The sign-in must survive one rather than fail.
    /// </summary>
    [Fact]
    public async Task Login_ADeviceSignInToldToSlowDown_KeepsPollingAndCompletes()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        var polls = 0;

        deployment.AnswerTokenRequest = _ =>
        {
            polls++;

            return polls switch
            {
                1 => FakeOAuthDeployment.Refusing("authorization_pending"),
                2 => FakeOAuthDeployment.Refusing("slow_down"),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        FakeOAuthDeployment.TokenResponse("an-access-token", "a-refresh-token", 3600),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                },
            };
        };

        using var handler = deployment.Handler();
        var store = this.CreateStore();

        var signIn = RunAsync(
            this.Context(store, handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "device",
            "--client-id",
            ClientId);

        // Act
        await this.AdvanceUntilCompleteAsync(signIn);

        // Assert
        Assert.Equal(0, await signIn);
        Assert.Equal(3, polls);
        Assert.Equal("an-access-token", store.Resolve(requestedDeployment: null).Token);
    }

    /// <summary>An OAuth sign-in with nothing to identify the client would fail at the authorization server, which is a worse place to learn it.</summary>
    [Fact]
    public async Task Login_AnOAuthModeWithoutAClientIdentifier_IsRefusedBeforeAnythingIsFetched()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();

        // Act
        var exitCode = await RunAsync(
            this.Context(this.CreateStore(), handler, FakeMailboxRedirect.Silent()),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "interactive");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(this.console.Errors, line => line.Contains("--client-id", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    private static Task<int> RunAsync(CliContext context, params string[] args) => CliRunner.RunAsync(context, args);

    /// <summary>Runs an interactive sign-in whose redirect echoes whatever value the command generated.</summary>
    private Task<int> RunInteractiveAsync(CredentialStore store, FakeHttpMessageHandler handler) =>
        RunAsync(
            this.Context(
                store,
                handler,
                FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated)),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "interactive",
            "--client-id",
            ClientId);

    /// <summary>Lets a polling device sign-in make progress, by moving the clock its delays are taken from.</summary>
    private async Task AdvanceUntilCompleteAsync(Task<int> signIn)
    {
        for (var attempt = 0; attempt < 20 && !signIn.IsCompleted; attempt++)
        {
            this.clock.Advance(TimeSpan.FromSeconds(1));

            await Task.Yield();
        }
    }

    /// <summary>Reads the anti-forgery value out of the address the command printed, the way the person's browser does.</summary>
    private string StateTheCommandGenerated()
    {
        var address = this.AuthorizationAddress();

        return HttpUtility.ParseQueryString(new Uri(address).Query)["state"]
            ?? throw new InvalidOperationException("The printed authorization address carried no state.");
    }

    private string AuthorizationAddress() =>
        this.console.Errors.LastOrDefault(line => line.Contains("state=", StringComparison.Ordinal))?.Trim()
        ?? throw new InvalidOperationException("The command printed no authorization address.");

    private CredentialStore CreateStore() => new(
        Path.Combine(this.storeDirectory, "credentials.json"),
        new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

    private CliContext Context(
        CredentialStore store,
        FakeHttpMessageHandler handler,
        Func<Uri, IMailboxRedirectAwaiter> awaitRedirect) => new(
        this.console,
        store,
        (endpoint, trust) => FakeDeploymentTransport.Over(handler, endpoint, trust),
        awaitRedirect,

        // Never started in a test: opening a browser is a side effect on the machine running the suite.
        _ => false,
        this.clock);
}
