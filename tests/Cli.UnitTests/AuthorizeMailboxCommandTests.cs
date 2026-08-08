// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the mailbox authorization command decides, with the browser and the socket substituted.</summary>
public sealed class AuthorizeMailboxCommandTests : IDisposable
{
    private readonly RecordingCliConsole console = new() { SecretToSupply = "a-client-secret" };
    private readonly string storeDirectory = Path.Combine(
        Path.GetTempPath(),
        $"mailfathom-authorize-{Guid.NewGuid():N}");

    [Fact]
    public async Task Authorize_UnknownProviderPreset_ReportsItWithoutReachingTheNetwork()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();

        // Act
        var exitCode = await this.RunAsync(handler, FakeMailboxRedirect.Silent(), "mailbox", "authorize", "--provider", "fastmail", "--client-id", "app");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("'fastmail' is not a known provider preset", StringComparison.Ordinal));
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task Authorize_GoogleWithTheDeviceGrant_RefusesBecauseNoMailScopeIsObtainableThroughIt()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.Silent(),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--mode", "device");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("does not issue mail scopes through the device flow", StringComparison.Ordinal));
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task Authorize_RedirectCarryingTheServersRefusal_ReportsThatRatherThanRedeemingAnything()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();
        var redirect = FakeMailboxRedirect.Answering(new MailboxRedirect(Code: null, State: null, Error: "access_denied"));

        // Act
        var exitCode = await this.RunAsync(handler, redirect, "mailbox", "authorize", "--provider", "google", "--client-id", "app");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("access_denied", StringComparison.Ordinal));
        Assert.Empty(handler.RecordedRequests);
    }

    /// <summary>
    /// The anti-forgery value is what ties the redirect to the authorization this run started, so a code arriving
    /// without it is refused before the token endpoint is asked to redeem it.
    /// </summary>
    [Fact]
    public async Task Authorize_RedirectEchoingAForeignState_RefusesWithoutRedeemingTheCode()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();
        var redirect = FakeMailboxRedirect.Approving("code-from-another-run", "a-state-this-run-never-issued");

        // Act
        var exitCode = await this.RunAsync(handler, redirect, "mailbox", "authorize", "--provider", "google", "--client-id", "app");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("state_mismatch", StringComparison.Ordinal));
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task Authorize_RedirectWithNeitherCodeNorError_IsRefusedRatherThanTreatedAsApproval()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();
        var redirect = FakeMailboxRedirect.Answering(new MailboxRedirect(Code: null, State: null, Error: null));

        // Act
        var exitCode = await this.RunAsync(handler, redirect, "mailbox", "authorize", "--provider", "google", "--client-id", "app");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task Authorize_InteractiveMode_ListensOnTheRedirectAddressItWasGiven()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();
        Uri? listenedOn = null;
        var redirect = FakeMailboxRedirect.Answering(new MailboxRedirect(Code: null, State: null, Error: "access_denied"));

        // Act
        await this.RunAsync(
            handler,
            address =>
            {
                listenedOn = address;

                return redirect(address);
            },
            "mailbox", "authorize", "--provider", "google", "--client-id", "app",
            "--redirect-uri", "http://127.0.0.1:9123/");

        // Assert
        Assert.Equal(new Uri("http://127.0.0.1:9123/"), listenedOn);
    }

    /// <summary>
    /// The preset records that Google rejects an exchange carrying no client secret. Honoring the flag anyway would
    /// send the request without the field and leave the operator reading the authorization server's own
    /// <c>invalid_client</c> instead of the reason for it.
    /// </summary>
    [Fact]
    public async Task Authorize_PublicClientAgainstAProviderThatRequiresASecret_IsRefusedBeforeTheExchange()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.Silent(),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--public-client");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("--public-client cannot be used with it", StringComparison.Ordinal));
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task Authorize_ARedirectAddressThatIsNotAnAddress_IsReportedAsOneLineRatherThanAParserFailure()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.Silent(),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app",
            "--redirect-uri", "127.0.0.1:8765");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("is not an address", StringComparison.Ordinal));
    }

    /// <summary>
    /// The command's core security contract: the refresh token is the only thing on standard output, so redirecting it
    /// captures the secret alone and every instruction around it reaches the person instead. A regression that put
    /// guidance on the wrong stream would be invisible to every other test here, because all of them refuse the
    /// exchange and never reach a grant.
    /// </summary>
    [Fact]
    public async Task Authorize_ASuccessfulExchange_PutsTheRefreshTokenOnStandardOutputAndNothingElse()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"an-access-token","refresh_token":"a-refresh-token","expires_in":3600}""",
                Encoding.UTF8,
                "application/json"),
        });

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app");

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(["a-refresh-token"], this.console.Lines);
        Assert.DoesNotContain(this.console.Errors, line => line.Contains("a-refresh-token", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole point of <c>--account</c>: the token reaches the deployment and no stream. A regression that printed
    /// it as well would leave it in the scrollback and in any session log, which is the exposure the option removes.
    /// </summary>
    [Fact]
    public async Task Authorize_WithAnAccount_SendsTheTokenToTheDeploymentAndPrintsItNowhere()
    {
        // Arrange
        this.SignInTo("production", "https://mail.example.test:8443", "an-admin-key");
        using var handler = ProviderIssuingAGrantAndDeploymentAnswering(HttpStatusCode.NoContent);

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--account", "workspace");

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.DoesNotContain(this.console.Lines, line => line.Contains("a-refresh-token", StringComparison.Ordinal));
        Assert.DoesNotContain(this.console.Errors, line => line.Contains("a-refresh-token", StringComparison.Ordinal));
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("Stored the refresh token for account 'workspace' on 'production'", StringComparison.Ordinal));
    }

    /// <summary>The request the deployment receives is the other half of the contract: the route, the credential, and the body.</summary>
    [Fact]
    public async Task Authorize_WithAnAccount_PostsTheGrantToTheWriteRouteUnderTheStoredCredential()
    {
        // Arrange
        this.SignInTo("production", "https://mail.example.test:8443", "an-admin-key");
        using var handler = ProviderIssuingAGrantAndDeploymentAnswering(HttpStatusCode.NoContent);

        // Act
        await this.RunAsync(
            handler,
            FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--account", "workspace");

        // Assert
        var sent = handler.RecordedRequests.Single(
            recorded => recorded.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailboxRefreshTokenPath);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("Bearer an-admin-key", sent.Headers["Authorization"][0]);

        using var body = JsonDocument.Parse(sent.ContentAsUtf8String());
        Assert.Equal("workspace", body.RootElement.GetProperty("account").GetString());
        Assert.Equal("a-refresh-token", body.RootElement.GetProperty("refreshToken").GetString());
    }

    /// <summary>
    /// A sign-in the deployment has nowhere to put is worse than no sign-in at all: somebody has already approved
    /// access at the provider, and the credential that produced it is then discarded unread.
    /// </summary>
    [Fact]
    public async Task Authorize_WithAnAccountAndNoProfile_FailsBeforeAnybodyIsAskedToApproveAnything()
    {
        // Arrange
        using var handler = TokenEndpointRefusing();

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.Silent(),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--account", "workspace");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Null(this.console.LastPrompt);
    }

    /// <summary>An account the deployment does not configure is refused by it, and its sentence is what the operator needs.</summary>
    [Fact]
    public async Task Authorize_AnAccountTheDeploymentDoesNotConfigure_ReportsWhatItSaid()
    {
        // Arrange
        this.SignInTo("production", "https://mail.example.test:8443", "an-admin-key");
        using var handler = ProviderIssuingAGrantAndDeploymentRefusing(
            HttpStatusCode.BadRequest,
            """{"detail":"This deployment configures no mail account named 'archive'."}""");

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--account", "archive");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("configures no mail account named 'archive'", StringComparison.Ordinal));
        Assert.DoesNotContain(this.console.Lines, line => line.Contains("a-refresh-token", StringComparison.Ordinal));
    }

    /// <summary>A deployment that refuses the administrative credential is reported as that rather than as a stored grant.</summary>
    [Fact]
    public async Task Authorize_ADeploymentRefusingTheAdministrativeCredential_SaysSoAndStoresNothing()
    {
        // Arrange
        this.SignInTo("production", "https://mail.example.test:8443", "a-revoked-key");
        using var handler = ProviderIssuingAGrantAndDeploymentRefusing(HttpStatusCode.Unauthorized, string.Empty);

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--account", "workspace");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("refused the credential", StringComparison.Ordinal));
        Assert.DoesNotContain(this.console.Lines, line => line.Contains("a-refresh-token", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each of these reaches an operator as a documented sentence, and each names a different thing to go and look at:
    /// a listener serving something else, a refusal with no reason in it, and an answer that is neither.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "", "serves no administrative endpoint")]
    [InlineData(HttpStatusCode.BadRequest, "", "refused the grant without saying why")]
    [InlineData(HttpStatusCode.BadRequest, "<html>refused</html>", "refused the grant without saying why")]
    [InlineData(HttpStatusCode.InternalServerError, "", "rather than storing the token")]
    public async Task Authorize_ADeploymentThatDoesNotStoreTheGrant_ReportsWhichKindOfAnswerItGave(
        HttpStatusCode deploymentStatus,
        string deploymentBody,
        string expectedReport)
    {
        // Arrange
        this.SignInTo("production", "https://mail.example.test:8443", "an-admin-key");
        using var handler = ProviderIssuingAGrantAndDeploymentRefusing(deploymentStatus, deploymentBody);

        // Act
        var exitCode = await this.RunAsync(
            handler,
            FakeMailboxRedirect.ApprovingWhenAsked("an-authorization-code", this.StateTheCommandGenerated),
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--account", "workspace");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains(expectedReport, StringComparison.Ordinal));
        Assert.DoesNotContain(this.console.Lines, line => line.Contains("a-refresh-token", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("code=abc&state=xyz", "abc", "xyz", null)]
    [InlineData("?code=abc&state=xyz", "abc", "xyz", null)]
    [InlineData("error=access_denied&state=xyz", null, "xyz", "access_denied")]
    [InlineData("code=&state=", null, null, null)]
    [InlineData("", null, null, null)]
    [InlineData(null, null, null, null)]
    public void FromQuery_WhateverTheBrowserArrivedWith_ReportsEachFieldOrItsAbsence(
        string? query,
        string? expectedCode,
        string? expectedState,
        string? expectedError)
    {
        // Arrange, Act
        var redirect = MailboxRedirect.FromQuery(query);

        // Assert
        Assert.Equal(expectedCode, redirect.Code);
        Assert.Equal(expectedState, redirect.State);
        Assert.Equal(expectedError, redirect.Error);
    }

    /// <summary>The code is redeemable for a refresh token, so it must not reach a log through a default rendering.</summary>
    [Fact]
    public void ToString_ARedirectCarryingACode_RedactsIt()
    {
        // Arrange
        var redirect = new MailboxRedirect("an-authorization-code", "a-state", Error: null);

        // Act
        var rendered = redirect.ToString();

        // Assert
        Assert.Equal("***", rendered);
        Assert.DoesNotContain("an-authorization-code", rendered, StringComparison.Ordinal);
    }

    /// <summary>A routable redirect address would let anything that can reach this host deliver an authorization code.</summary>
    [Fact]
    public void LoopbackRedirectAwaiter_ARoutableAddress_IsRefusedBeforeAnythingIsBound()
    {
        // Arrange, Act
        var failure = Assert.Throws<CliFailure>(() => new LoopbackRedirectAwaiter(new Uri("http://mail.example.test:8765/")));

        // Assert
        Assert.Contains("is not a loopback address", failure.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    /// <summary>Reads the anti-forgery value out of the authorization address the command printed, the way a browser would.</summary>
    private string StateTheCommandGenerated()
    {
        var address = this.console.Errors.LastOrDefault(line => line.Contains("state=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The command printed no authorization address to read the state from.");

        return HttpUtility.ParseQueryString(new Uri(address.Trim()).Query)["state"]
            ?? throw new InvalidOperationException("The printed authorization address carried no state.");
    }

    /// <summary>Remembers a deployment the way <c>login</c> would, so a command has a profile to act through.</summary>
    private void SignInTo(string name, string endpoint, string token) =>
        new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")))
            .Save(name, new Uri(endpoint), token, "workstation");

    /// <summary>Answers as the provider's token endpoint and as the deployment, told apart by the path.</summary>
    /// <remarks>
    /// One handler serves every transport a command opens, so a run that sends to both is only meaningful if the two
    /// answer differently. Routing on the administrative prefix is also what lets a test assert that the grant reached
    /// the write route rather than merely that two requests happened.
    /// </remarks>
    private static FakeHttpMessageHandler ProviderIssuingAGrantAndDeploymentAnswering(HttpStatusCode deploymentStatus) =>
        ProviderIssuingAGrantAndDeploymentRefusing(deploymentStatus, string.Empty);

    private static FakeHttpMessageHandler ProviderIssuingAGrantAndDeploymentRefusing(
        HttpStatusCode deploymentStatus,
        string deploymentBody) =>
        new((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath.StartsWith(AdminEndpointRoutes.Prefix, StringComparison.Ordinal) == true
                ? new HttpResponseMessage(deploymentStatus)
                {
                    Content = new StringContent(deploymentBody, Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"an-access-token","refresh_token":"a-refresh-token","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json"),
                }));

    /// <summary>A token endpoint that refuses everything, so a test reaching it fails loudly rather than passing quietly.</summary>
    private static FakeHttpMessageHandler TokenEndpointRefusing() =>
        FakeHttpMessageHandler.AlwaysResponding(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_request"}""", Encoding.UTF8, "application/json"),
        });

    private Task<int> RunAsync(
        FakeHttpMessageHandler handler,
        Func<Uri, IMailboxRedirectAwaiter> awaitRedirect,
        params string[] args) =>
        CliRunner.RunAsync(
            new CliContext(
                this.console,
                new CredentialStore(
                    Path.Combine(this.storeDirectory, "credentials.json"),
                    new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key"))),
                (endpoint, trust) => FakeDeploymentTransport.Over(handler, endpoint, trust),
                awaitRedirect,
                // Never started in a test: opening a browser is a side effect on the machine running the suite.
                _ => false,
                new FakeTimeProvider()),
            args);
}
