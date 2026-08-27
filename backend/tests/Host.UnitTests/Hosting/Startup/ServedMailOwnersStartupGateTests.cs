// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Startup;

/// <summary>
/// Covers how a start settles who this deployment serves: the owners a file declares, the rows the database holds, and
/// the reconciliation between them that gives each declared owner the row every mail account of theirs hangs on.
/// </summary>
public sealed class ServedMailOwnersStartupGateTests
{
    private static readonly Guid DeclaredIdentifier = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task StartAsync_NoOwnerDeclaredAndOneRowHeld_ServesThatOwnerFromTheDeploymentSection()
    {
        // Arrange
        var roster = new ServedMailOwners();

        // Act
        await CreateGate([Held(SyntheticMailOwner.Deployment, "owner")], servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(SyntheticMailOwner.Deployment, served.Owner);
        Assert.Equal(MailOwnerAccountSource.DeploymentSection, served.Source);
        Assert.Equal(SyntheticMailOwner.Deployment, roster.Owner);
    }

    /// <summary>
    /// The release's own migration provisions that row, so reaching this means the row is not there at all. Generating
    /// one is what keeps the deployment's configured mailboxes belonging to somebody rather than failing the start.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoOwnerDeclaredAndNoRowHeld_RecordsOneUnderAGeneratedVersionFourIdentifier()
    {
        // Arrange
        var provisioning = Substitute.For<IMailOwnerProvisioning>();
        var roster = new ServedMailOwners();

        // Act
        await CreateGate([], provisioning: provisioning, servedOwners: roster).StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(4, served.Owner.Value.Version);
        await provisioning.Received(1).ProvisionAsync(served.Owner, "owner", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Several rows and no declaration is a deployment whose mailboxes are still in the section that names no owner, so
    /// nothing could say which of them a configured account is for.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoOwnerDeclaredAndSeveralRowsHeld_FailsStartupNamingWhereToDeclareThem()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([Held(SyntheticMailOwner.Deployment, "owner"), Held(SyntheticMailOwner.Another, "second")])
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.DeploymentMailOwnerUnresolved, refusal.ErrorCode);
        Assert.Contains("Declare each owner in the top-level Accounts collection", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_AnOwnerDeclaredWithNoRow_GivesThemTheRowTheMailGraphHangsOn()
    {
        // Arrange
        var provisioning = Substitute.For<IMailOwnerProvisioning>();
        var roster = new ServedMailOwners();

        // Act
        await CreateGate([], Declaring(DeclaredIdentifier, "alex"), provisioning, servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        await provisioning.Received(1)
            .ProvisionAsync(MailOwnerId.Create(DeclaredIdentifier), "alex", Arg.Any<CancellationToken>());
        var served = Assert.Single(roster.Owners);
        Assert.Equal(MailOwnerAccountSource.OwnerDeclaration, served.Source);
    }

    /// <summary>A label is what an administrator reads a roster by rather than anything an account hangs on, so a file that renames an owner renames them.</summary>
    [Fact]
    public async Task StartAsync_ADeclaredOwnerRelabelled_PutsTheNewLabelOnTheRowTheyAlreadyHold()
    {
        // Arrange
        var provisioning = Substitute.For<IMailOwnerProvisioning>();

        // Act
        await CreateGate(
                [Held(MailOwnerId.Create(DeclaredIdentifier), "alexandra")],
                Declaring(DeclaredIdentifier, "alex"),
                provisioning)
            .StartAsync(CancellationToken.None);

        // Assert
        await provisioning.Received(1)
            .RelabelAsync(MailOwnerId.Create(DeclaredIdentifier), "alex", Arg.Any<CancellationToken>());
        await provisioning.DidNotReceive()
            .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The identifier is what every mail account, every stored message, and every job of theirs hangs on, so a
    /// declaration that changed it would leave all of it belonging to nobody.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeclaredIdentifierChangedForAnOwnerAlreadyHeld_FailsStartupNamingTheOwner()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([Held(SyntheticMailOwner.Deployment, "alex")], Declaring(DeclaredIdentifier, "alex"))
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Restore the identifier the deployment holds", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A label names one owner, and the unique index on the column is what says so. A relabel onto a label another
    /// held owner still carries is refused in a sentence rather than met as a constraint violation the operator would
    /// read as PostgreSQL's.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeclaredLabelAnotherHeldOwnerStillCarries_FailsStartupNamingTheLabel()
    {
        // Arrange
        var provisioning = Substitute.For<IMailOwnerProvisioning>();

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [Held(MailOwnerId.Create(DeclaredIdentifier), "alexandra"), Held(SyntheticMailOwner.Another, "alex")],
                    Declaring(DeclaredIdentifier, "alex"),
                    provisioning)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Free the label first", refusal.Message, StringComparison.Ordinal);
        await provisioning.DidNotReceive()
            .RelabelAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An owner the deployment holds and no file declares keeps their mail and stops being served, which is a report rather than a refusal.</summary>
    [Fact]
    public async Task StartAsync_AHeldOwnerNoFileDeclares_LeavesThemOutOfTheRosterWithoutFailing()
    {
        // Arrange
        var roster = new ServedMailOwners();

        // Act
        await CreateGate(
                [Held(MailOwnerId.Create(DeclaredIdentifier), "alex"), Held(SyntheticMailOwner.Another, "somebody else")],
                Declaring(DeclaredIdentifier, "alex"),
                servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(MailOwnerId.Create(DeclaredIdentifier), served.Owner);
    }

    /// <summary>
    /// An owner-facing surface answers one person about their own mail, and nothing this release admits a caller with
    /// names the owner they act for — neither the absence of a credential nor a configured one.
    /// </summary>
    [Theory]
    [InlineData(true, "it admits callers without authenticating them")]
    [InlineData(false, "a configured credential authenticates a caller without naming the owner")]
    public async Task StartAsync_SeveralOwnersServedWithAnOwnerFacingSurfaceEnabled_FailsStartupSayingWhy(
        bool authenticationDisabled,
        string reasonNamed)
    {
        // Arrange
        var mcp = new McpEndpointOptions { Enabled = true };

        if (!authenticationDisabled)
        {
            mcp.Authentication.Add(new());
        }

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [],
                    TwoDeclaredOwners(),
                    mcpEndpointSettings: mcp)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains(reasonNamed, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A deployment that serves no owner-facing surface synchronizes several owners' mail perfectly well.</summary>
    [Fact]
    public async Task StartAsync_SeveralOwnersServedWithNoOwnerFacingSurface_ServesEveryOneOfThem()
    {
        // Arrange
        var roster = new ServedMailOwners();

        // Act
        await CreateGate([], TwoDeclaredOwners(), servedOwners: roster).StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, roster.Owners.Count);
    }

    /// <summary>
    /// The marker is what an adoption sets, and from then on that owner's mailboxes are the document's rather than the
    /// file's — permanently, and for that owner alone.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnOwnerWhoseDocumentWasWrittenAtRuntime_ServesThemFromItRatherThanTheirDeclaration()
    {
        // Arrange
        var owner = MailOwnerId.Create(DeclaredIdentifier);
        var roster = new ServedMailOwners();
        var documents = DocumentsHolding(
            owner,
            """
            {"MailAccounts":[{"AccountId":"adopted","DisplayName":"Adopted at work","Host":"imap.example.test",
            "UserName":"alex@example.test",
            "Secrets":{"Password":{"SecretReference":"systemd-credential:imap-adopted-password"}}}]}
            """);

        // Act
        await CreateGate(
                [Adopted(owner, "alex")],
                Declaring(DeclaredIdentifier, "alex"),
                servedOwners: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(MailOwnerAccountSource.OwnerDocument, served.Source);
        Assert.False(served.ReadFromConfiguration);
        Assert.Equal(["adopted"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>
    /// The alternative to failing is a deployment quietly synchronizing the mailboxes an adoption was meant to replace,
    /// because the owner's declared section has stopped being read and their document says nothing usable.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnAdoptedOwnerWhoseDocumentWillNotBind_FailsStartupNamingTheOwner()
    {
        // Arrange
        var owner = MailOwnerId.Create(DeclaredIdentifier);
        var documents = DocumentsHolding(owner, """{"MailAccounts":[{"AccountId":"adopted","Nonsense":"no property binds this"}]}""");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([Adopted(owner, "alex")], Declaring(DeclaredIdentifier, "alex"), documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("served from it rather than from configuration", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_AServedRoster_ReportsTheOwnerGateToTheStartupProbe()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailOwners);

        // Act
        await CreateGate([Held(SyntheticMailOwner.Deployment, "owner")], startupGates: startupGates)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.True(startupGates.Completed);
    }

    /// <summary>A gate that failed took the host down with it, so nothing may report the host as having come up.</summary>
    [Fact]
    public async Task StartAsync_ARosterItCannotServe_LeavesTheOwnerGateOutstanding()
    {
        // Arrange
        var startupGates = new HostStartupGates(HostStartupGate.ServedMailOwners);

        // Act
        await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [Held(SyntheticMailOwner.Deployment, "owner"), Held(SyntheticMailOwner.Another, "second")],
                    startupGates: startupGates)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.False(startupGates.Completed);
    }

    /// <summary>Reading one row more than a deployment may hold is what makes a roster past the bound observable rather than silently truncated.</summary>
    [Fact]
    public async Task StartAsync_AlwaysGiven_ReadsOneOwnerMoreThanADeploymentMayHold()
    {
        // Arrange
        var directory = DirectoryOf([Held(SyntheticMailOwner.Deployment, "owner")]);

        // Act
        await CreateGate(directory).StartAsync(CancellationToken.None);

        // Assert
        await directory.Received(1)
            .ReadOwnersAsync(DeclaredOwners.MaximumDeclaredOwners + 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_TheCallersToken_PropagatesItToTheDirectory()
    {
        // Arrange
        var directory = DirectoryOf([Held(SyntheticMailOwner.Deployment, "owner")]);
        using var cancellation = new CancellationTokenSource();

        // Act
        await CreateGate(directory).StartAsync(cancellation.Token);

        // Assert
        await directory.Received(1).ReadOwnersAsync(Arg.Any<int>(), cancellation.Token);
    }

    private static MailOwnerRecord Held(MailOwnerId owner, string displayName) =>
        new(owner, displayName, DocumentWrittenAtRuntime: false);

    /// <summary>An owner whose document an adoption has written, which is what makes it the source their mailboxes come from.</summary>
    private static MailOwnerRecord Adopted(MailOwnerId owner, string displayName) =>
        new(owner, displayName, DocumentWrittenAtRuntime: true);

    private static IOwnerSettingsDocumentReader DocumentsHolding(MailOwnerId owner, string json)
    {
        var documents = Substitute.For<IOwnerSettingsDocumentReader>();

        documents.ReadAsync(owner, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<OwnerSettingsDocument?>(
                new OwnerSettingsDocument(owner, "alex", json, Version: 2, WrittenAtRuntime: true)));

        return documents;
    }

    private static IConfiguration Declaring(Guid identifier, string displayName) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.Id)}"] = identifier.ToString(),
                [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.DisplayName)}"] = displayName,
            })
            .Build();

    private static IConfiguration TwoDeclaredOwners() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.Id)}"] = DeclaredIdentifier.ToString(),
                [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.DisplayName)}"] = "alex",
                [$"{DeclaredOwnerOptions.SectionName}:1:{nameof(DeclaredOwnerOptions.Id)}"] = SyntheticMailOwner.Another.Value.ToString(),
                [$"{DeclaredOwnerOptions.SectionName}:1:{nameof(DeclaredOwnerOptions.DisplayName)}"] = "sam",
            })
            .Build();

    private static IMailOwnerDirectory DirectoryOf(IReadOnlyList<MailOwnerRecord> held)
    {
        var directory = Substitute.For<IMailOwnerDirectory>();

        directory.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(held));

        return directory;
    }

    private static ServedMailOwnersStartupGate CreateGate(
        IReadOnlyList<MailOwnerRecord> held,
        IConfiguration? declared = null,
        IMailOwnerProvisioning? provisioning = null,
        ServedMailOwners? servedOwners = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IOwnerSettingsDocumentReader? documents = null) =>
        CreateGate(DirectoryOf(held), declared, provisioning, servedOwners, startupGates, mcpEndpointSettings, documents);

    private static ServedMailOwnersStartupGate CreateGate(
        IMailOwnerDirectory directory,
        IConfiguration? declared = null,
        IMailOwnerProvisioning? provisioning = null,
        ServedMailOwners? servedOwners = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IOwnerSettingsDocumentReader? documents = null)
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => directory);
        services.AddScoped(_ => provisioning ?? Substitute.For<IMailOwnerProvisioning>());
        services.AddScoped(_ => documents ?? Substitute.For<IOwnerSettingsDocumentReader>());
        services.AddSingleton(new OwnerAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider()));

        return new ServedMailOwnersStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            declared ?? new ConfigurationBuilder().Build(),
            servedOwners ?? new ServedMailOwners(),
            startupGates ?? new HostStartupGates(HostStartupGate.ServedMailOwners),
            Options.Create(mcpEndpointSettings ?? new McpEndpointOptions()),
            Options.Create(new ClientEndpointOptions()),
            new FakeTimeProvider(),
            NullLogger<ServedMailOwnersStartupGate>.Instance);
    }
}
