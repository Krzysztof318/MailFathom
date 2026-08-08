// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using MailFathom.Cli.Authorization;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the two ways a deployment's transport can be weaker than the default, and what accepting one costs.</summary>
/// <remarks>
/// <para>
/// Both questions are asked once, at <c>login</c>, and what they leave behind is a record on the profile rather than a
/// protection turned off. The claims worth defending are that refusing either stores nothing and signs in to nothing,
/// that an accepted certificate narrows the profile to exactly that certificate, and that no later command asks again.
/// </para>
/// <para>
/// Driven through the command rather than against <see cref="ClearTextDecision" /> and
/// <see cref="SignInConnection" /> directly, because what each of those decides is only meaningful as a step of a
/// sign-in: whether anything was sent before the question, whether the credential was read twice, and what ended up in
/// the store. A test of either in isolation could pass while the sign-in around it did the wrong thing.
/// </para>
/// </remarks>
public sealed class TransportTrustSignInTests : IDisposable
{
    private const string SecureEndpoint = "https://mail.example.test:8443";

    private const string ClearTextEndpoint = "http://mail.example.test:8080";

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-transport-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));

    private readonly X509Certificate2 authority = TestCertificates.CreateCertificateAuthority("MailFathom test authority");

    private readonly X509Certificate2 deploymentCertificate;

    private readonly X509Certificate2 replacementCertificate;

    /// <summary>What a connection whose handshake was refused answers, which is nothing: the request never reaches the deployment.</summary>
    private readonly FakeHttpMessageHandler refusedHandshake = FakeAdminEndpoint.Unreachable();

    /// <summary>Every connection a command opened, in order, so a test can assert which trust travelled to which address.</summary>
    /// <remarks>
    /// By address rather than by host, because the authorization server serves both the discovery document and the token
    /// endpoint: keying on the host would let a correctly opened token-endpoint connection stand in for a discovery
    /// connection that was never opened, which is exactly the regression these assertions exist to catch.
    /// </remarks>
    private readonly List<(Uri Address, StoredTransportTrust Trust)> openedConnections = [];

    public TransportTrustSignInTests()
    {
        this.deploymentCertificate = TestCertificates.IssueServerCertificate(this.authority, "mail.example.test");
        this.replacementCertificate = TestCertificates.IssueServerCertificate(this.authority, "mail.example.test");
    }

    [Fact]
    public async Task Login_AnUntrustedCertificateAccepted_PinsItToTheProfileAndSignsIn()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await RunAsync(
            this.PresentingContext(store, handler), "login", "--endpoint", SecureEndpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.deploymentCertificate),
            store.Resolve(requestedDeployment: null).Trust.PinnedCertificateFingerprint);
        Assert.Contains(this.console.Questions, question => question.Contains("Trust this certificate", StringComparison.Ordinal));
    }

    /// <summary>The operator has to see what they are accepting, or the question is a prompt to press y at.</summary>
    [Fact]
    public async Task Login_AnUntrustedCertificate_ShowsItsSubjectIssuerFingerprintValidityAndWhyItFailed()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswerToGive = true;

        // Act
        await RunAsync(this.PresentingContext(this.CreateStore(), handler), "login", "--endpoint", SecureEndpoint);

        // Assert
        var reported = string.Join('\n', this.console.Errors);
        Assert.Contains(this.deploymentCertificate.Subject, reported, StringComparison.Ordinal);
        Assert.Contains(this.deploymentCertificate.Issuer, reported, StringComparison.Ordinal);
        Assert.Contains(PresentedCertificate.FingerprintOf(this.deploymentCertificate), reported, StringComparison.Ordinal);
        Assert.Contains(
            this.deploymentCertificate.NotAfter.ToUniversalTime().ToString("u"),
            reported,
            StringComparison.Ordinal);
        Assert.Contains("does not trust the chain", reported, StringComparison.Ordinal);
        Assert.Contains(
            this.console.Errors,
            line => line.StartsWith($"{SecureEndpoint} presented a certificate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_AnUntrustedCertificateRefused_StoresNothingAndFails()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await RunAsync(this.PresentingContext(store, handler), "login", "--endpoint", SecureEndpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("certificate was refused", StringComparison.Ordinal));
    }

    /// <summary>A sign-in with nobody at the terminal must never prompt: the answer would be read out of whatever was piped in.</summary>
    [Fact]
    public async Task Login_AnUntrustedCertificateWithNoTerminal_FailsNamingTheSwitchThatWouldHaveAllowedIt()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await RunAsync(this.PresentingContext(store, handler), "login", "--endpoint", SecureEndpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Empty(this.console.Questions);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("--trust-untrusted-certificate", StringComparison.Ordinal));
    }

    /// <summary>The switch weakens the one sign-in and not the profile it produces: what the deployment presented is still pinned.</summary>
    [Fact]
    public async Task Login_AnUntrustedCertificateAllowedUpFront_PinsItWithoutAskingAnything()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await RunAsync(
            this.PresentingContext(store, handler),
            "login",
            "--endpoint",
            SecureEndpoint,
            "--trust-untrusted-certificate");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(this.console.Questions);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.deploymentCertificate),
            store.Resolve(requestedDeployment: null).Trust.PinnedCertificateFingerprint);
    }

    /// <summary>A deployment with a certificate this machine trusts signs in as it always did, and records nothing.</summary>
    [Fact]
    public async Task Login_ACertificateThisMachineTrusts_AsksNothingAndPinsNothing()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", SecureEndpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(this.console.Questions);
        Assert.Equal(StoredTransportTrust.Protected, store.Resolve(requestedDeployment: null).Trust);
    }

    [Fact]
    public async Task Login_AClearTextEndpointAccepted_RecordsItAndSaysWhatIsUnprotected()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", ClearTextEndpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(store.Resolve(requestedDeployment: null).Trust.AcceptsClearText);
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("clear text", StringComparison.Ordinal));

        // The scheme is part of the line, because that is what an operator compares their terminal against.
        Assert.Contains(
            this.console.Errors,
            line => line.StartsWith($"{ClearTextEndpoint} is an HTTP address", StringComparison.Ordinal));
    }

    /// <summary>The question is asked from the address alone, before anything is sent, so a deployment that would have redirected never gets to answer it.</summary>
    [Fact]
    public async Task Login_AClearTextEndpointThatWouldRedirectToHttps_IsStillAskedAboutBeforeAnythingIsSent()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Answering(HttpStatusCode.MovedPermanently);
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", ClearTextEndpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(this.console.Questions, question => question.Contains("unprotected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_AClearTextEndpointRefused_StoresNothingAndSaysWhichProtectionWasRefused()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", ClearTextEndpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("Transport protection was refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_AClearTextEndpointWithNoTerminal_FailsNamingTheSwitchThatWouldHaveAllowedIt()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", ClearTextEndpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(this.console.Errors, line => line.Contains("--allow-clear-text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_AClearTextEndpointAllowedUpFront_SignsInWithoutAskingAnything()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "not-a-real-key";
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await RunAsync(
            this.Context(store, handler), "login", "--endpoint", ClearTextEndpoint, "--allow-clear-text");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(this.console.Questions);
        Assert.True(store.Resolve(requestedDeployment: null).Trust.AcceptsClearText);
    }

    /// <summary>The decision is taken once. A command that asked again would train an operator to answer without reading.</summary>
    [Fact]
    public async Task Status_AClearTextProfile_ProceedsWithoutAskingAgain()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save(
            "production",
            new Uri(ClearTextEndpoint),
            "not-a-real-key",
            "workstation",
            trust: new StoredTransportTrust(PinnedCertificateFingerprint: null, AcceptsClearText: true));
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(this.console.Questions);
    }

    /// <summary>What the pin is for: the deployment substituted its certificate, and the command says so rather than reporting a connection failure.</summary>
    [Fact]
    public async Task Status_APinnedProfileMeetingADifferentCertificate_RefusesAndNamesTheChange()
    {
        // Arrange
        var pinned = PresentedCertificate.FingerprintOf(this.deploymentCertificate);
        var store = this.CreateStore();
        store.Save(
            "production",
            new Uri(SecureEndpoint),
            "not-a-real-key",
            "workstation",
            trust: new StoredTransportTrust(pinned));
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");

        // Act
        var exitCode = await RunAsync(
            this.PresentingContext(store, handler, this.replacementCertificate), "status");

        // Assert: the deployment would have answered, so the refusal is the pin's and nothing was sent.
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(this.console.Errors, line => line.Contains(pinned, StringComparison.Ordinal));
        Assert.Contains(
            this.console.Errors,
            line => line.Contains(
                PresentedCertificate.FingerprintOf(this.replacementCertificate),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Status_APinnedProfileMeetingThePinnedCertificate_ReachesTheDeployment()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save(
            "production",
            new Uri(SecureEndpoint),
            "not-a-real-key",
            "workstation",
            trust: new StoredTransportTrust(PresentedCertificate.FingerprintOf(this.deploymentCertificate)));
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");

        // Act
        var exitCode = await RunAsync(
            this.PresentingContext(store, handler, this.deploymentCertificate), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(this.console.Questions);
    }

    /// <summary>A renewed certificate is accepted by signing in again, which is what re-asks rather than trusting it silently.</summary>
    [Fact]
    public async Task Login_APinnedProfileWhoseCertificateChanged_AsksAgainAndPinsTheNewOne()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save(
            "production",
            new Uri(SecureEndpoint),
            "the-old-key",
            "workstation",
            trust: new StoredTransportTrust(PresentedCertificate.FingerprintOf(this.deploymentCertificate)));
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.5.0");
        this.console.SecretToSupply = "the-new-key";
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await RunAsync(
            this.PresentingContext(store, handler, this.replacementCertificate),
            "login",
            "--endpoint",
            "production");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.replacementCertificate),
            store.Resolve("production").Trust.PinnedCertificateFingerprint);
    }

    /// <summary>
    /// A profile's pin names the deployment's certificate and says nothing about the authorization server's, so a
    /// silent renewal must reach that server under ordinary chain validation. Applying the pin there instead would
    /// refuse every renewal for exactly the operators this feature exists for, and it would do it on an ordinary
    /// command rather than at sign-in, where nothing would say which certificate was the problem.
    /// </summary>
    [Fact]
    public async Task Status_APinnedProfileWhoseAccessTokenExpired_RenewsAgainstTheAuthorizationServersOwnCertificate()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();
        store.Save(
            "production",
            new Uri(SecureEndpoint),
            "a-spent-access-token",
            "workstation",
            new OAuthSession(
                "a-refresh-token",
                this.clock.GetUtcNow().AddMinutes(-1),
                new Uri(FakeOAuthDeployment.TokenEndpoint),
                FakeOAuthDeployment.Issuer,
                "mfctl",
                FakeOAuthDeployment.Resource,
                "mailfathom.admin"),
            trust: new StoredTransportTrust(PresentedCertificate.FingerprintOf(this.deploymentCertificate)));

        // Act: the deployment presents the pinned certificate, the authorization server its own trusted one.
        var exitCode = await RunAsync(this.TwoHostContext(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("refresh_token", deployment.LastTokenRequest["grant_type"]);
        Assert.Equal("an-access-token", store.Resolve("production").Token);
        Assert.Equal(
            StoredTransportTrust.Protected,
            this.TrustCarriedTo(new Uri(FakeOAuthDeployment.TokenEndpoint)));
    }

    /// <summary>The same isolation for a clear-text profile: an unprotected deployment does not make the renewal unprotected.</summary>
    [Fact]
    public async Task Status_AClearTextProfileWhoseAccessTokenExpired_RenewsWithoutCarryingTheClearTextDecision()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();
        store.Save(
            "production",
            new Uri(ClearTextEndpoint),
            "a-spent-access-token",
            "workstation",
            new OAuthSession(
                "a-refresh-token",
                this.clock.GetUtcNow().AddMinutes(-1),
                new Uri(FakeOAuthDeployment.TokenEndpoint),
                FakeOAuthDeployment.Issuer,
                "mfctl",
                FakeOAuthDeployment.Resource,
                "mailfathom.admin"),
            trust: new StoredTransportTrust(PinnedCertificateFingerprint: null, AcceptsClearText: true));

        // Act
        var exitCode = await RunAsync(this.TwoHostContext(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("refresh_token", deployment.LastTokenRequest["grant_type"]);
        Assert.Equal(
            StoredTransportTrust.Protected,
            this.TrustCarriedTo(new Uri(FakeOAuthDeployment.TokenEndpoint)));
    }

    /// <summary>
    /// The same isolation one step earlier, and on the path where the pin does not exist yet. A sign-in reads the
    /// deployment's metadata through the connection whose certificate is still in question and the authorization
    /// server's through one of its own, so threading the deployment's transport into both would refuse every OAuth
    /// sign-in to a self-signed deployment — at the authorization server, which is the wrong machine to send an
    /// operator to look at.
    /// </summary>
    [Fact]
    public async Task Login_AnInteractiveSignInToAnUntrustedDeployment_DiscoversTheAuthorizationServerUnpinned()
    {
        // Arrange
        var deployment = FakeOAuthDeployment.Answering();
        using var handler = deployment.Handler();
        var store = this.CreateStore();
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await RunAsync(
            this.TwoHostContext(store, handler, FakeMailboxRedirect.ApprovingWhenAsked(
                "an-authorization-code",
                this.StateTheCommandGenerated)),
            "login",
            "--endpoint",
            FakeOAuthDeployment.DeploymentAddress,
            "--mode",
            "interactive",
            "--client-id",
            "mfctl");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("an-access-token", store.Resolve(requestedDeployment: null).Token);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.deploymentCertificate),
            store.Resolve(requestedDeployment: null).Trust.PinnedCertificateFingerprint);
        Assert.Equal(StoredTransportTrust.Protected, this.TrustCarriedToTheDiscoveryDocument());
    }

    /// <summary>Reports the trust one address was reached under, failing when no connection was opened to it at all.</summary>
    private StoredTransportTrust TrustCarriedTo(Uri address) =>
        this.openedConnections.SingleOrDefault(opened => opened.Address == address).Trust
        ?? throw new InvalidOperationException($"No connection was opened to {address}.");

    /// <summary>Reports the trust the authorization server's discovery document was fetched under.</summary>
    /// <remarks>Matched on the metadata address rather than on the host, so a token-endpoint connection opened correctly cannot stand in for a discovery connection that was never opened.</remarks>
    private StoredTransportTrust TrustCarriedToTheDiscoveryDocument() =>
        this.openedConnections
            .Where(opened => opened.Address.AbsolutePath.StartsWith("/.well-known/", StringComparison.Ordinal))
            .Select(opened => opened.Trust)
            .FirstOrDefault()
        ?? throw new InvalidOperationException("No connection was opened to fetch a discovery document.");

    private static Task<int> RunAsync(CliContext context, params string[] args) =>
        CliRunner.RunAsync(context, args);

    /// <summary>Reads the anti-forgery value out of the address the command printed, the way the person's browser does.</summary>
    private string StateTheCommandGenerated()
    {
        var address = this.console.Errors.LastOrDefault(line => line.Contains("state=", StringComparison.Ordinal))?.Trim()
            ?? throw new InvalidOperationException("The command printed no authorization address.");

        return HttpUtility.ParseQueryString(new Uri(address).Query)["state"]
            ?? throw new InvalidOperationException("The printed authorization address carried no state.");
    }

    private CredentialStore CreateStore() => new(
        Path.Combine(this.storeDirectory, "credentials.json"),
        new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

    /// <summary>Builds a context for a deployment whose certificate never comes up.</summary>
    private CliContext Context(CredentialStore store, FakeHttpMessageHandler handler) => new(
        this.console,
        store,
        (address, trust) => FakeDeploymentTransport.Over(handler, address, trust),
        FakeMailboxRedirect.Silent(),
        _ => false,
        this.clock);

    /// <summary>Builds a context for a deployment presenting one certificate to every connection a command opens.</summary>
    /// <remarks>
    /// A connection the policy refuses answers nothing, which is what a failed handshake looks like from above: the
    /// request never reaches the deployment. So a test asserting that a command completed is asserting that the profile's
    /// own trust is what carried it, and one asserting a failure is asserting that nothing was sent.
    /// </remarks>
    private CliContext PresentingContext(
        CredentialStore store,
        FakeHttpMessageHandler handler,
        X509Certificate2? presented = null) => new(
        this.console,
        store,
        (address, trust) => FakeDeploymentTransport.Presenting(
            address,
            trust,
            presented ?? this.deploymentCertificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            handler,
            this.refusedHandshake),
        FakeMailboxRedirect.Silent(),
        _ => false,
        this.clock);

    /// <summary>Builds a context for the two hosts an OAuth sign-in reaches: the deployment, and the server that authorizes it.</summary>
    /// <remarks>
    /// The authorization server presents a certificate of its own that this machine trusts, which is the ordinary case
    /// and the one that fails loudly if the deployment's trust ever travelled here: it names the deployment's
    /// certificate, so a pinned connection would refuse this one and the request — a discovery document at sign-in, a
    /// renewal afterwards — would never be sent.
    /// </remarks>
    private CliContext TwoHostContext(
        CredentialStore store,
        FakeHttpMessageHandler handler,
        Func<Uri, IMailboxRedirectAwaiter>? awaitRedirect = null) => new(
        this.console,
        store,
        (address, trust) =>
        {
            this.openedConnections.Add((address, trust));

            // An http address completes no handshake, so no certificate is presented over one and the ordinary
            // transport is what a command meets there.
            if (address.Scheme == Uri.UriSchemeHttp)
            {
                return FakeDeploymentTransport.Over(handler, address, trust);
            }

            var reachesTheDeployment = address.Host == new Uri(SecureEndpoint).Host;

            return FakeDeploymentTransport.Presenting(
                address,
                trust,
                reachesTheDeployment ? this.deploymentCertificate : this.replacementCertificate,
                reachesTheDeployment ? SslPolicyErrors.RemoteCertificateChainErrors : SslPolicyErrors.None,
                handler,
                this.refusedHandshake);
        },
        awaitRedirect ?? FakeMailboxRedirect.Silent(),

        // Never started in a test: opening a browser is a side effect on the machine running the suite.
        _ => false,
        this.clock);

    public void Dispose()
    {
        this.authority.Dispose();
        this.deploymentCertificate.Dispose();
        this.replacementCertificate.Dispose();
        this.refusedHandshake.Dispose();

        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
