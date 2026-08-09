// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailFathom.Cli.Credentials;
using MailFathom.Common.ClientAssertions;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers signing in with a key pair, and what every later command then presents.</summary>
/// <remarks>
/// The property worth defending is that nothing reusable is ever written down. A profile signed in this way stores no
/// credential at all, and each command signs a fresh short-lived assertion from a key that stays where the operator put
/// it — so a credentials file that leaves the machine carries nothing an attacker could present.
/// </remarks>
public sealed class KeyPairSignInTests : IDisposable
{
    private const string Endpoint = "https://mail.example.test:8443";

    private readonly string workingDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-keypair-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The credential store must hold nothing presentable, which is the whole reason to sign in this way.</summary>
    [Fact]
    public async Task Login_WithAKeyPair_StoresTheKeyLocationAndNoCredential()
    {
        // Arrange
        var store = this.CreateStore();
        var privateKeyPath = this.WriteKeyPair();
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");

        // Act
        var exitCode = await RunAsync(
            this.Context(store, handler),
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            privateKeyPath);

        // Assert
        Assert.Equal(0, exitCode);

        var stored = Assert.Single(store.Read().Profiles).Value;
        Assert.Equal(privateKeyPath, stored.KeyPair?.PrivateKeyPath);
        Assert.Equal(string.Empty, store.Resolve(requestedDeployment: null).Token);
    }

    /// <summary>Signing in proves the deployment holds the matching public half, which nothing on this machine can decide alone.</summary>
    [Fact]
    public async Task Login_WithAKeyPair_PresentsAnAssertionTheDeploymentCanVerify()
    {
        // Arrange
        var privateKeyPath = this.WriteKeyPair();
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");

        // Act
        await RunAsync(
            this.Context(this.CreateStore(), handler),
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            privateKeyPath);

        // Assert
        var presented = handler.LastAuthorization();

        Assert.Equal("Bearer", presented?.Scheme);
        Assert.Equal(ClientAssertion.DeclaredType, HeaderOf(presented?.Parameter).GetProperty("typ").GetString());
    }

    /// <summary>Every command mints its own, because a stored one would be the reusable credential this mode exists to avoid.</summary>
    [Fact]
    public async Task Status_AKeyPairProfile_MintsAFreshAssertionForTheRequest()
    {
        // Arrange
        var store = this.CreateStore();
        var privateKeyPath = this.WriteKeyPair();
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");

        await RunAsync(
            this.Context(store, handler),
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            privateKeyPath);

        var signedInWith = handler.LastAuthorization()?.Parameter;

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);

        var presented = handler.LastAuthorization()?.Parameter;

        Assert.NotNull(presented);
        Assert.NotEqual(signedInWith, presented);
        Assert.Equal(
            ClientAssertion.AdminAudience,
            PayloadOf(presented).GetProperty(ClientAssertion.AudienceClaimName).GetString());
    }

    /// <summary>A profile is used from whatever directory a later command runs in, which is rarely the one it was signed in from, so what is stored has to be absolute.</summary>
    [Fact]
    public async Task Login_WithAKeyPair_StoresAnAbsoluteKeyPath()
    {
        // Arrange
        var store = this.CreateStore();
        var privateKeyPath = this.WriteKeyPair();
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");

        // Act
        await RunAsync(
            this.Context(store, handler),
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            privateKeyPath);

        // Assert
        Assert.True(Path.IsPathRooted(Assert.Single(store.Read().Profiles).Value.KeyPair?.PrivateKeyPath));
    }

    /// <summary>Naming the mode without the key is the mistake to name plainly, rather than reaching the deployment with nothing to sign.</summary>
    [Fact]
    public async Task Login_WithAKeyPairAndNoKey_SaysWhatToPassWithoutReachingTheDeployment()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");

        // Act
        var exitCode = await RunAsync(
            this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint, "--mode", "keypair");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(this.console.Errors, line => line.Contains("--private-key", StringComparison.Ordinal));
    }

    /// <summary>Handing the command the half the deployment registers is the likeliest mistake, so it is the one told apart from every other.</summary>
    [Fact]
    public async Task Login_WithThePublicHalfOfTheKeyPair_SaysWhichHalfToPass()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");
        var publicKeyPath = this.WriteKeyPair().Replace(".key", ".pub", StringComparison.Ordinal);

        // Act
        var exitCode = await RunAsync(
            this.Context(this.CreateStore(), handler),
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            publicKeyPath);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(this.console.Errors, line => line.Contains("public key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WithAKeyThatIsNotThere_ReportsTheFileRatherThanCrashing()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("nightly-digest");

        // Act
        var exitCode = await RunAsync(
            this.Context(this.CreateStore(), handler),
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            Path.Combine(this.workingDirectory, "absent.key"));

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.workingDirectory))
        {
            Directory.Delete(this.workingDirectory, recursive: true);
        }
    }

    private static Task<int> RunAsync(CliContext context, params string[] args) => CliRunner.RunAsync(context, args);

    private static JsonElement HeaderOf(string? assertion) => SegmentOf(assertion, 0);

    private static JsonElement PayloadOf(string? assertion) => SegmentOf(assertion, 1);

    private static JsonElement SegmentOf(string? assertion, int index)
    {
        Assert.NotNull(assertion);

        return JsonDocument.Parse(Base64Url.DecodeFromChars(assertion.Split('.')[index])).RootElement;
    }

    /// <summary>Writes a key pair to the test's own directory and reports where the private half landed.</summary>
    private string WriteKeyPair()
    {
        Directory.CreateDirectory(this.workingDirectory);

        using var pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var privateKeyPath = Path.Combine(this.workingDirectory, "nightly.key");

        File.WriteAllText(privateKeyPath, pair.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);
        File.WriteAllText(
            Path.Combine(this.workingDirectory, "nightly.pub"),
            pair.ExportSubjectPublicKeyInfoPem(),
            Encoding.ASCII);

        return privateKeyPath;
    }

    private CredentialStore CreateStore() => new(
        Path.Combine(this.workingDirectory, "credentials.json"),
        new TokenProtector(Path.Combine(this.workingDirectory, "credentials.key")));

    private CliContext Context(CredentialStore store, FakeHttpMessageHandler handler) => new(
        this.console,
        store,
        (endpoint, trust) => FakeDeploymentTransport.Over(handler, endpoint, trust),
        FakeMailboxRedirect.Silent(),
        _ => false,
        this.clock);
}
