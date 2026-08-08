// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Cryptography.X509Certificates;
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
            this.CertificateContext(store, handler), "login", "--endpoint", SecureEndpoint);

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
        await RunAsync(this.CertificateContext(this.CreateStore(), handler), "login", "--endpoint", SecureEndpoint);

        // Assert
        var reported = string.Join('\n', this.console.Errors);
        Assert.Contains(this.deploymentCertificate.Subject, reported, StringComparison.Ordinal);
        Assert.Contains(this.deploymentCertificate.Issuer, reported, StringComparison.Ordinal);
        Assert.Contains(PresentedCertificate.FingerprintOf(this.deploymentCertificate), reported, StringComparison.Ordinal);
        Assert.Contains(
            new DateTimeOffset(this.deploymentCertificate.NotAfter).ToString("u"),
            reported,
            StringComparison.Ordinal);
        Assert.Contains("does not trust the chain", reported, StringComparison.Ordinal);
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
        var exitCode = await RunAsync(this.CertificateContext(store, handler), "login", "--endpoint", SecureEndpoint);

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
        var exitCode = await RunAsync(this.CertificateContext(store, handler), "login", "--endpoint", SecureEndpoint);

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
            this.CertificateContext(store, handler),
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
        using var handler = FakeAdminEndpoint.Unreachable();

        // Act
        var exitCode = await RunAsync(
            this.PresentingContext(store, handler, this.replacementCertificate), "status");

        // Assert
        Assert.Equal(1, exitCode);
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
            this.CertificateContext(store, handler, this.replacementCertificate),
            "login",
            "--endpoint",
            "production");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.replacementCertificate),
            store.Resolve("production").Trust.PinnedCertificateFingerprint);
    }

    private static Task<int> RunAsync(CliContext context, params string[] args) =>
        CliRunner.RunAsync(context, args);

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
    private CliContext PresentingContext(
        CredentialStore store,
        FakeHttpMessageHandler handler,
        X509Certificate2 presented) => new(
        this.console,
        store,
        (address, trust) => FakeDeploymentTransport.Presenting(handler, address, trust, presented),
        FakeMailboxRedirect.Silent(),
        _ => false,
        this.clock);

    /// <summary>Builds a context for a deployment whose certificate this machine refuses until the profile pins it.</summary>
    /// <remarks>
    /// The refusing connection answers nothing, which is what a failed handshake looks like from above: the request never
    /// reaches the deployment. Only the connection opened after the pin was taken can carry one, so a test asserting that
    /// the sign-in completed is asserting that the pin is what carried it.
    /// </remarks>
    private CliContext CertificateContext(
        CredentialStore store,
        FakeHttpMessageHandler handler,
        X509Certificate2? presented = null)
    {
        var certificate = presented ?? this.deploymentCertificate;

        return new CliContext(
            this.console,
            store,
            (address, trust) => FakeDeploymentTransport.Presenting(
                trust.PinnedCertificateFingerprint is null ? this.refusedHandshake : handler,
                address,
                trust,
                certificate),
            FakeMailboxRedirect.Silent(),
            _ => false,
            this.clock);
    }

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
