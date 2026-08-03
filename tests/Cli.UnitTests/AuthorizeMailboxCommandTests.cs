// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the mailbox authorization command decides, with the browser and the socket substituted.</summary>
public sealed class AuthorizeMailboxCommandTests : IDisposable
{
    private readonly RecordingCliConsole console = new();
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
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--mode", "device", "--public-client");

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
        var exitCode = await this.RunAsync(handler, redirect, "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--public-client");

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
        var exitCode = await this.RunAsync(handler, redirect, "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--public-client");

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
        var exitCode = await this.RunAsync(handler, redirect, "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--public-client");

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
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--public-client",
            "--redirect-uri", "http://127.0.0.1:9123/");

        // Assert
        Assert.Equal(new Uri("http://127.0.0.1:9123/"), listenedOn);
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
            "mailbox", "authorize", "--provider", "google", "--client-id", "app", "--public-client",
            "--redirect-uri", "127.0.0.1:8765");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("is not an address", StringComparison.Ordinal));
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
                endpoint => new HttpClient(handler, disposeHandler: false) { BaseAddress = endpoint },
                awaitRedirect,
                // Never started in a test: opening a browser is a side effect on the machine running the suite.
                _ => false),
            args);
}
