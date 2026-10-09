// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>
/// Covers what a start settles about who this deployment serves: whether it holds nobody, one user, or several, and the
/// refusal a deployment whose user-facing surfaces could not tell several apart is stopped by. Composing a user from
/// their record is <see cref="ServedUserResolution" />'s, and happens the first time something asks for them.
/// </summary>
public sealed class ServedUsersStartupGateTests
{
    /// <summary>
    /// The state a fresh database is in, and so is a deployment whose every user was erased, and one a start
    /// admits: it starts, completes its gate, serves nobody, and says so. Refusing here would leave that deployment
    /// with no start to record a user from.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoUserHeld_SaysHowToRecordOneAndCompletesTheGate()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedUsers);
        var startupLog = new RecordingLogger<ServedUsersStartupGate>();

        // Act
        await CreateGate(RowsOf(), startupGates: startupGates, startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(startupGates.Completed);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("serves no user", StringComparison.Ordinal)
                && message.Contains("mfctl user add", StringComparison.Ordinal));
    }

    /// <summary>
    /// A switch left on with nothing to synchronize is what every deployment looks like between its first start and
    /// its first recorded mailbox, so it is reported and the deployment runs.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndNoAccountServed_ReportsItRatherThanRefusing()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedUsers);
        var startupLog = new RecordingLogger<ServedUsersStartupGate>();

        // Act
        await CreateGate(
                RowsOf(SyntheticUser.Deployment),
                SynchronizationSwitchedOn(),
                startupGates: startupGates,
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(startupGates.Completed);
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("nothing to synchronize", StringComparison.Ordinal));
    }

    /// <summary>The control for the report above: an account being served leaves nothing to say about synchronization.</summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndAnAccountServed_ReportsNothingAboutSynchronization()
    {
        // Arrange
        var startupLog = new RecordingLogger<ServedUsersStartupGate>();
        var servedAccounts = ServedAccountsReading(new ServedMailAccountVersion(Guid.NewGuid(), 1));

        // Act
        await CreateGate(
                RowsOf(SyntheticUser.Deployment),
                SynchronizationSwitchedOn(),
                servedAccounts: servedAccounts,
                startupLog: startupLog)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            startupLog.Messages,
            message => message.Contains("nothing to synchronize", StringComparison.Ordinal));
    }

    /// <summary>
    /// A user-facing surface answers one person about their own mail, and a surface requiring no credential admits a
    /// caller who names nobody — which leaves the user to be supplied by the deployment, and it has one to supply only
    /// while it serves one person.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralUsersHeldWithAUserFacingSurfaceAuthenticatingNobody_FailsStartupSayingWhy()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentUserUnresolvedException>(() =>
            CreateGate(
                    TwoUsers(),
                    mcpEndpointSettings: new McpEndpointOptions { Enabled = true })
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("requires no authentication", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The client surface is user-facing on exactly the terms the MCP one is, so a deployment holding several users
    /// is refused for having it open unauthenticated even where no MCP endpoint is served at all.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralUsersHeldWithTheClientEndpointAuthenticatingNobody_FailsStartupSayingWhy()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentUserUnresolvedException>(() =>
            CreateGate(
                    TwoUsers(),
                    clientEndpointSettings: new ClientEndpointOptions { Enabled = true })
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("requires no authentication", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal above counts users rather than forbidding the posture, so a deployment serving one person or nobody
    /// keeps an unauthenticated surface — which is what lets a first run reach a client with no credential and record
    /// the person it is for.
    /// </summary>
    /// <param name="held">Whether the deployment holds its first user yet.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_AtMostOneUserOnAnUnauthenticatedUserFacingSurface_Starts(bool held)
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedUsers);

        // Act
        await CreateGate(
                held ? RowsOf(SyntheticUser.Deployment) : RowsOf(),
                startupGates: startupGates,
                mcpEndpointSettings: new McpEndpointOptions { Enabled = true },
                clientEndpointSettings: new ClientEndpointOptions { Enabled = true })
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>
    /// An administrator acts for the deployment rather than for a person, so every user-scoped act of theirs names the
    /// user it is for and none of them has to be resolved from the users held. The administrative surface is therefore
    /// not among the ones this refusal reads, which is what makes recording a second user reachable at all.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralUsersHeldWithNoUserFacingSurfaceEnabled_Starts()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedUsers);

        // Act
        await CreateGate(TwoUsers(), startupGates: startupGates)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>
    /// Every credential a user-facing surface admits is a record naming its user, whichever method presents it, so a
    /// surface requiring one can say which user every caller acts for — which is the posture under which a deployment
    /// may serve several people, and it holds for each of the four methods rather than for one of them.
    /// </summary>
    /// <param name="method">The method the enabled surface accepts.</param>
    [Theory]
    [InlineData("password")]
    [InlineData("api-key")]
    [InlineData("public-key")]
    [InlineData("oauth-subject")]
    public async Task StartAsync_SeveralUsersHeldWhereTheUserFacingSurfaceRequiresACredential_Starts(string method)
    {
        // Arrange
        var mcp = new McpEndpointOptions { Enabled = true };
        var startupGates = new HostStartupGates(HostStartupGate.ServedUsers);

        mcp.Authentication.Add(new() { Method = method });

        // Act
        await CreateGate(TwoUsers(), startupGates: startupGates, mcpEndpointSettings: mcp)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>The count the gate reads is what names the sole user afterwards, so a caller naming nobody acts for them.</summary>
    [Fact]
    public async Task StartAsync_OneUserHeld_LeavesThemTheSoleUser()
    {
        // Arrange
        var servedUsers = ResolvedServedUsers.Over(RowsOf(SyntheticUser.Deployment));

        // Act
        await CreateGate(servedUsers).StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticUser.Deployment, servedUsers.User);
    }

    /// <summary>A gate that failed took the host down with it, so nothing may report the host as having come up.</summary>
    [Fact]
    public async Task StartAsync_ADeploymentItRefuses_LeavesTheUserGateOutstanding()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedUsers);

        // Act
        await Assert.ThrowsAsync<DeploymentUserUnresolvedException>(() =>
            CreateGate(
                    TwoUsers(),
                    startupGates: startupGates,
                    mcpEndpointSettings: new McpEndpointOptions { Enabled = true })
                .StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.False(startupGates.Completed);
    }

    [Fact]
    public async Task StartAsync_TheCallersToken_PropagatesItToTheRows()
    {
        // Arrange
        var documents = RowsOf(SyntheticUser.Deployment);
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(documents).StartAsync(cancellation.Token);

        // Assert
        await documents.Received(1).ReadVersionsAsync(Arg.Any<int>(), cancellation.Token);
    }

    /// <summary>The rows a deployment holds, each at version one, answered however many the caller asks for.</summary>
    private static IUserSettingsDocumentReader RowsOf(params UserId[] users)
    {
        var documents = Substitute.For<IUserSettingsDocumentReader>();
        IReadOnlyList<UserSettingsDocumentVersion> versions = [.. users.Select(static user => new UserSettingsDocumentVersion(user, 1))];

        documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<UserSettingsDocumentVersion>>([.. versions.Take(callInfo.ArgAt<int>(0))]));

        return documents;
    }

    private static IUserSettingsDocumentReader TwoUsers() => RowsOf(SyntheticUser.Deployment, SyntheticUser.Another);

    private static IServedMailAccountReader ServedAccountsReading(params ServedMailAccountVersion[] versions)
    {
        var servedAccounts = Substitute.For<IServedMailAccountReader>();

        servedAccounts.ReadServedVersionsAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ServedMailAccountVersion>>(versions));

        return servedAccounts;
    }

    /// <summary>The one switch a deployment with no mailbox recorded yet is most likely to have turned on.</summary>
    private static IConfiguration SynchronizationSwitchedOn() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "true",
            })
            .Build();

    private static ServedUsersStartupGate CreateGate(
        IUserSettingsDocumentReader documents,
        IConfiguration? configuration = null,
        IServedMailAccountReader? servedAccounts = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedUsersStartupGate>? startupLog = null) =>
        CreateGate(
            ResolvedServedUsers.Over(documents),
            configuration,
            servedAccounts,
            startupGates,
            mcpEndpointSettings,
            clientEndpointSettings,
            startupLog);

    private static ServedUsersStartupGate CreateGate(
        ServedUsers servedUsers,
        IConfiguration? configuration = null,
        IServedMailAccountReader? servedAccounts = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedUsersStartupGate>? startupLog = null) =>
        new(
            servedUsers,
            servedAccounts ?? ServedAccountsReading(),
            configuration ?? new ConfigurationBuilder().Build(),
            startupGates ?? new HostStartupGates(HostStartupGate.ServedUsers),
            new SeveralUserAdmission(
                Options.Create(mcpEndpointSettings ?? new McpEndpointOptions()),
                Options.Create(clientEndpointSettings ?? new ClientEndpointOptions())),
            startupLog ?? NullLogger<ServedUsersStartupGate>.Instance);
}
