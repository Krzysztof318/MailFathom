// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
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

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Login_ADeploymentThatAcceptsTheCredential_StoresItAndReportsWhoTheCallerIs()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("not-a-real-key", store.Resolve(requestedDeployment: null).Token);
        Assert.Contains(this.console.Lines, line => line.Contains("workstation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_ThePresentedCredential_IsSentAsABearerCredential()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        await RunAsync(this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal("Bearer", handler.LastAuthorization()?.Scheme);
        Assert.Equal("not-a-real-key", handler.LastAuthorization()?.Parameter);
        Assert.Equal("/api/admin/session", handler.LastPath());
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
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(this.console.Errors, line => line.Contains("refused the credential", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment from another release line must not reach the store either. The credential itself is fine there —
    /// what is wrong is the deployment, and a profile saved for one every later command would refuse is a sign-in that
    /// succeeded into nothing.
    /// </summary>
    [Fact]
    public async Task Login_ADeploymentFromAnotherReleaseLine_StoresNothingAndFails()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation", FakeAdminEndpoint.AnotherReleaseLine);
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("another release line", StringComparison.Ordinal));
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
        Assert.Empty(store.Read().Profiles);
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

    /// <summary>
    /// The address answered, but on a listener serving something else. This is the message the troubleshooting table
    /// documents, and the one that sends an operator to look at the port rather than at their key.
    /// </summary>
    [Fact]
    public async Task Login_AnAddressServingNoAdministrativeEndpoint_SaysToCheckThePort()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Answering(HttpStatusCode.NotFound);
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("serves no administrative endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_ADeploymentThatCannotBeReached_ReportsTheTransportFailureRatherThanCrashing()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Unreachable();
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(this.console.Errors, line => line.Contains("could not be reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment that accepts the connection and never answers is a different problem from one nothing is listening
    /// at, and the message says which: the address and the port are right, so the operator looks at the deployment.
    /// </summary>
    [Fact]
    public async Task Login_ADeploymentThatNeverAnswers_ReportsTheTimeoutRatherThanCrashing()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Silent();
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(store.Read().Profiles);
        Assert.Contains(this.console.Errors, line => line.Contains("did not answer in time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_NoCredentialSupplied_FailsWithoutReachingTheDeployment()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = string.Empty;

        // Act
        var exitCode = await RunAsync(this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
    }

    /// <summary>The profile is named after the host unless the operator says otherwise, so one address needs no second argument.</summary>
    [Fact]
    public async Task Login_NoNameGiven_RemembersTheDeploymentUnderItsHostName()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        await RunAsync(this.Context(store, handler), "login", "--endpoint", Endpoint);

        // Assert
        Assert.Equal("mail.example.test", Assert.Single(store.Read().Profiles).Key);
    }

    [Fact]
    public async Task Login_ANameGiven_RemembersTheDeploymentUnderItAndSelectsIt()
    {
        // Arrange
        var store = this.CreateStore();
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(
            this.Context(store, handler), "login", "--endpoint", Endpoint, "--name", "production");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("production", store.Resolve(requestedDeployment: null).Name);
    }

    /// <summary>Replacing a rotated credential should not mean retyping the address it belongs to.</summary>
    [Fact]
    public async Task Login_AnExistingProfileByName_SignsInAgainAtTheAddressItAlreadyHolds()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "the-old-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "the-new-key";

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "login", "--endpoint", "production");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("the-new-key", store.Resolve("production").Token);
        Assert.Single(store.Read().Profiles);
    }

    /// <summary>A name that is neither a stored profile nor an address is a mistake, and saying which it was is the fix.</summary>
    [Fact]
    public async Task Login_AnUnknownNameThatIsNotAnAddress_SaysToPassAnAddressInstead()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(this.Context(this.CreateStore(), handler), "login", "--endpoint", "qa");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("neither a stored profile nor an endpoint address", StringComparison.Ordinal));
    }

    /// <summary>--endpoint reads an absolute address as an address, so a profile named like one could never be selected.</summary>
    [Fact]
    public async Task Login_ANameThatIsAnAddress_IsRefused()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");
        this.console.SecretToSupply = "not-a-real-key";

        // Act
        var exitCode = await RunAsync(
            this.Context(this.CreateStore(), handler), "login", "--endpoint", Endpoint, "--name", "https://elsewhere");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task Logout_AProfileThatWasStored_IsForgotten()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");

        // Act
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        var exitCode = await RunAsync(this.Context(store, handler), "logout", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(store.Read().Profiles);
    }

    /// <summary>
    /// The behavior every command owes an operator who has not signed in: a sentence naming what to run, rather than a
    /// transport failure from a request that was never going to carry a credential.
    /// </summary>
    [Theory]
    [InlineData("status")]
    [InlineData("logout")]
    public async Task ACommandNeedingACredential_WhenNotSignedIn_SaysHowToSignIn(string commandName)
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(this.CreateStore(), handler), commandName);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("Not signed in", StringComparison.Ordinal)
                && line.Contains("mfctl login", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Status_ADeploymentThatAcceptsTheStoredCredential_PresentsItAndReportsWhatItSaid()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("not-a-real-key", handler.LastAuthorization()?.Parameter);
        Assert.Contains(this.console.Lines, line => line.Contains("production", StringComparison.Ordinal));
    }

    /// <summary>
    /// The command that tells an operator what they are administering is also where they learn where to read about it,
    /// and the version that decides which pages those are is the deployment's rather than this command's.
    /// </summary>
    [Fact]
    public async Task Status_ADeploymentReportingAVersion_PointsAtTheDocumentationForThatVersion()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation", FakeAdminEndpoint.AnotherBuildOfThisLine);

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(
            this.console.Lines,
            line => line.Contains(
                "https://krzysztof318.github.io/MailFathom/latest/",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// An unreadable version is an absence of evidence rather than evidence of a break, which the version agreement
    /// already warns on rather than acts on. Naming a documentation directory for it would be a guess printed as fact.
    /// </summary>
    [Fact]
    public async Task Status_ADeploymentReportingNoReadableVersion_SaysNothingAboutDocumentation()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation", "unknown");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "status");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(
            this.console.Lines,
            line => line.Contains("krzysztof318.github.io", StringComparison.Ordinal));
    }

    /// <summary>The whole point of the override: one invocation goes elsewhere without changing what the next one does.</summary>
    [Fact]
    public async Task Status_WithAnEndpointOverride_ReachesThatProfileAndLeavesTheSelectionAlone()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("staging", new Uri("https://staging.example.test:8443"), "staging-key", "workstation");
        store.Save("production", EndpointAddress, "production-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "status", "--endpoint", "staging");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("staging-key", handler.LastAuthorization()?.Parameter);
        Assert.Equal("production", store.Resolve(requestedDeployment: null).Name);
    }

    [Fact]
    public async Task Switch_AStoredProfile_ChangesWhichOneLaterCommandsUse()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("staging", new Uri("https://staging.example.test:8443"), "staging-key", "workstation");
        store.Save("production", EndpointAddress, "production-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "switch", "staging");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(handler.RecordedRequests);
        Assert.Equal("staging", store.Resolve(requestedDeployment: null).Name);
    }

    [Fact]
    public async Task Switch_AnUnknownProfile_FailsAndLeavesTheSelectionAlone()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "production-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "switch", "qa");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Equal("production", store.Resolve(requestedDeployment: null).Name);
    }

    [Fact]
    public async Task Profiles_SomeStored_ListsThemAndMarksTheOneInUse()
    {
        // Arrange
        var store = this.CreateStore();
        store.Save("production", EndpointAddress, "production-key", "workstation");
        store.Save("staging", new Uri("https://staging.example.test:8443"), "staging-key", "workstation");
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(store, handler), "profiles");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Empty(handler.RecordedRequests);
        // The name is read out of the Profile column rather than out of the row, so a value that arrived under the wrong
        // heading fails here instead of passing on a row-wide match.
        var headings = Assert.Single(
            this.console.Lines,
            line => line.StartsWith("In use", StringComparison.Ordinal));
        var profileColumn = headings.IndexOf("Profile", StringComparison.Ordinal);

        Assert.Contains(
            this.console.Lines,
            row => !row.StartsWith('*') && NamesProfile(row, profileColumn, "production"));
        Assert.Contains(
            this.console.Lines,
            row => row.StartsWith('*') && NamesProfile(row, profileColumn, "staging"));
    }

    [Fact]
    public async Task Profiles_NoneStored_SaysSoRatherThanFailing()
    {
        // Arrange
        using var handler = FakeAdminEndpoint.Accepting("workstation");

        // Act
        var exitCode = await RunAsync(this.Context(this.CreateStore(), handler), "profiles");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(this.console.Lines, line => line.Contains("mfctl login", StringComparison.Ordinal));
    }

    private static Task<int> RunAsync(CliContext context, params string[] args) =>
        CliRunner.RunAsync(context, args);

    /// <summary>Reads whether one row of a listing carries the given value in the column starting at the given position.</summary>
    /// <remarks>
    /// The position comes from the heading row, so this asserts the cell rather than the row: a name that arrived under
    /// a different heading, or a name duplicated into an unrelated cell, is a regression and this is what sees it.
    /// </remarks>
    private static bool NamesProfile(string row, int profileColumn, string profile) =>
        row.Length > profileColumn && row[profileColumn..].StartsWith(profile, StringComparison.Ordinal);

    private CredentialStore CreateStore() => new(
        Path.Combine(this.storeDirectory, "credentials.json"),
        new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

    /// <summary>Builds the context a command runs under, with the terminal, the store, and the network all substituted.</summary>
    private CliContext Context(CredentialStore store, FakeHttpMessageHandler handler) => new(
        this.console,
        store,
        (endpoint, trust) => FakeDeploymentTransport.Over(handler, endpoint, trust),
        FakeMailboxRedirect.Silent(),
        _ => false,
        this.clock);

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
