// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what signing in does, and what it refuses to do.</summary>
/// <remarks>
/// The claim worth defending is that a credential is stored only after the deployment confirmed it. Without that,
/// <c>login</c> is a way of writing a file and the first sign that a key is wrong arrives at some later command.
/// </remarks>
public sealed class LoginCommandTests : IDisposable
{
    private const string Endpoint = "https://mail.example.test:8443";

    private static readonly Uri EndpointAddress = new(Endpoint);

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    [Fact]
    public async Task Login_ADeploymentThatAcceptsTheCredential_StoresItAndReportsWhoTheCallerIs()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.2.0");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("not-a-real-key", store.Find(EndpointAddress)!.Token);
        Assert.Contains(this.console.Lines, line => line.Contains("workstation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_ThePresentedCredential_IsSentAsABearerCredential()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.2.0");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        await RunAsync(this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
        Assert.Equal("not-a-real-key", handler.LastAuthorization?.Parameter);
        Assert.Equal("/api/admin/session", handler.LastPath);
    }

    /// <summary>A refused credential must not reach the store, or the next command would present one the deployment already rejected.</summary>
    [Fact]
    public async Task Login_ADeploymentThatRefusesTheCredential_StoresNothingAndFails()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Answering(HttpStatusCode.Unauthorized);
        this.console.SecretToSupply = "wrong-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Null(store.Find(EndpointAddress));
        Assert.Contains(this.console.Errors, line => line.Contains("refused the credential", StringComparison.Ordinal));
    }

    /// <summary>
    /// A success status is not by itself a MailFathom deployment: a proxy or an unrelated service on the same host can
    /// return one. Requiring the body to name the service is what keeps a stored credential from being one nothing saw.
    /// </summary>
    [Fact]
    public async Task Login_AnAddressAnsweringWithSomethingElse_IsNotTakenForADeployment()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.AnsweringBody(HttpStatusCode.OK, """{"service":"something-else"}""");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Null(store.Find(EndpointAddress));
    }

    [Fact]
    public async Task Login_AnAddressAnsweringWithSomethingThatIsNotJson_FailsWithoutACrash()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.AnsweringBody(HttpStatusCode.OK, "<html>a login page</html>");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Login_NoCredentialSupplied_FailsWithoutReachingTheDeployment()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.2.0");
        this.console.SecretToSupply = string.Empty;

        // Act
        var exitCode = await RunAsync(this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Logout_ACredentialThatWasStored_IsForgotten()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save(EndpointAddress, new StoredCredential("not-a-real-key", "workstation"));

        // Act
        using var handler = FakeAdminEndpoint.Accepting("workstation", "0.2.0");

        var exitCode = await RunAsync(this.Context(store, handler), "logout", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Null(store.Find(EndpointAddress));
    }

    private static Task<int> RunAsync(CliContext context, params string[] args) =>
        CliRunner.RunAsync(context, args);

    private CredentialStore CreateStore() =>
        new(Path.Combine(this.storeDirectory, "credentials.json"));

    /// <summary>Builds the context a command runs under, with the terminal, the store, and the network all substituted.</summary>
    private CliContext Context(CredentialStore store, FakeAdminEndpoint handler) => new(
        this.console,
        store,
        endpoint => new HttpClient(handler, disposeHandler: false) { BaseAddress = endpoint });

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
