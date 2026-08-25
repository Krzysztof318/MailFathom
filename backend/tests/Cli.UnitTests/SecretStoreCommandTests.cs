// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what an operator is told about where their credential ended up.</summary>
/// <remarks>
/// <see cref="SecretStoreCredentialTests" /> covers what the store does; this covers the two commands that have to say
/// so. Both sentences exist for the same reason: the arrangement an operator gets follows from what their machine
/// offers rather than from anything they chose, so a command that stayed silent would leave them to work it out from a
/// file — or, at <c>logout</c>, to not know an entry was left in their keyring at all.
/// </remarks>
public sealed class SecretStoreCommandTests : IDisposable
{
    private const string Endpoint = "https://mail.example.test:8443";

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));

    private readonly FakeOperatorSecretStore secretStore = new();

    [Fact]
    public async Task Login_AMachineWithASecretStore_SaysThePlatformHoldsTheCredential()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        await this.RunAsync(handler, "login", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("held by the platform's secret store", StringComparison.Ordinal));
    }

    /// <summary>The weaker arrangement taken silently is what this sentence exists to prevent.</summary>
    [Fact]
    public async Task Login_AMachineWithNoSecretStore_SaysTheCredentialIsSealedInTheFileAndWhy()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";
        this.secretStore.Refusal = "libsecret is not installed here";

        // Act
        await this.RunAsync(handler, "login", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("sealed in the credentials file", StringComparison.Ordinal)
                && line.Contains("libsecret is not installed here", StringComparison.Ordinal));
    }

    /// <summary>A key-pair profile keeps no credential anywhere, so there is no storage to report and no sentence to be wrong about.</summary>
    [Fact]
    public async Task Login_AKeyPairProfile_SaysNothingAboutWhereACredentialIsHeld()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        await this.RunAsync(
            handler,
            "login",
            "--endpoint",
            Endpoint,
            "--mode",
            "keypair",
            "--private-key",
            this.WriteKeyPair());

        // Assert
        Assert.DoesNotContain(
            this.console.Errors,
            line => line.Contains("The credential is", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_AProfileThePlatformHolds_ClearsItsEntriesAndSaysNothingAboutThem()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        await this.RunAsync(handler, "login", "--endpoint", Endpoint);

        // Act
        await this.RunAsync(handler, "logout");

        // Assert
        Assert.Empty(this.secretStore.Entries);
        Assert.Empty(this.console.Warnings);
    }

    /// <summary>An entry the command could not remove is the operator's to clear, and they can only do that if they hear about it.</summary>
    [Fact]
    public async Task Logout_AKeyringThatHasLockedSinceTheSignIn_ReportsWhatIsLeftBehind()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        await this.RunAsync(handler, "login", "--endpoint", Endpoint);

        this.secretStore.Refusal = "the collection is locked";

        // Act
        await this.RunAsync(handler, "logout");

        // Assert
        Assert.Contains(
            this.console.Warnings,
            line => line.Contains("still there", StringComparison.Ordinal)
                && line.Contains("the collection is locked", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    private Task<int> RunAsync(FakeHttpMessageHandler handler, params string[] args) =>
        CliRunner.RunAsync(this.Context(handler), args);

    /// <summary>Writes the key a key-pair sign-in signs its assertion with, which is a file rather than anything stored.</summary>
    private string WriteKeyPair()
    {
        Directory.CreateDirectory(this.storeDirectory);

        using var pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var privateKeyPath = Path.Combine(this.storeDirectory, "nightly.key");

        File.WriteAllText(privateKeyPath, pair.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);

        return privateKeyPath;
    }

    private CliContext Context(FakeHttpMessageHandler handler) => new(
        this.console,
        new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")),
            this.secretStore),
        (endpoint, trust) => FakeDeploymentTransport.Over(handler, endpoint, trust),
        FakeMailboxRedirect.Silent(),
        _ => false,
        this.clock);
}
