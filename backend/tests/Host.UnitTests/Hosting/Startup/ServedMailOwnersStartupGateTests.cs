// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var provisioning = ProvisioningThatRecords();
        var roster = new ServedMailOwners();

        // Act
        await CreateGate([], provisioning: provisioning, servedOwners: roster).StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(4, served.Owner.Value.Version);
        await provisioning.Received(1).ProvisionAsync(served.Owner, "owner", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Another replica of this deployment recorded the sole owner between this one reading an empty directory and its
    /// own insert reaching the table. Serving the identifier this process minted would hang every mail account, stored
    /// message, and job on a row that is not there.
    /// </summary>
    [Fact]
    public async Task StartAsync_TheSoleOwnerAnotherReplicaRecordedFirst_ServesTheRowTheDeploymentHolds()
    {
        // Arrange
        var roster = new ServedMailOwners();
        var winner = Held(SyntheticMailOwner.Another, "owner");

        var directory = Substitute.For<IMailOwnerDirectory>();
        directory.ReadOwnersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<MailOwnerRecord>>([]),
            Task.FromResult<IReadOnlyList<MailOwnerRecord>>([winner]));

        var provisioning = Substitute.For<IMailOwnerProvisioning>();
        provisioning
            .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Act
        await CreateGate(directory, provisioning: provisioning, servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(SyntheticMailOwner.Another, served.Owner);
        Assert.Equal("owner", served.DisplayName);
        Assert.Equal(MailOwnerAccountSource.DeploymentSection, served.Source);
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
        var provisioning = ProvisioningThatRecords();
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
        var provisioning = ProvisioningThatAccepts();

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
    /// A label taken between the roster being read and the relabel reaching the table is what no reading of a snapshot
    /// could refuse earlier, and the statement writes nothing rather than raising. A start that read that as success
    /// would go on serving an owner under a label another owner holds, which is the one thing the column's unique index
    /// exists to prevent.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeclaredLabelTakenWhileTheRelabelWasInFlight_FailsStartupNamingTheLabel()
    {
        // Arrange
        var provisioning = ProvisioningThatAccepts();

        provisioning
            .RelabelAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [Held(MailOwnerId.Create(DeclaredIdentifier), "alexandra")],
                    Declaring(DeclaredIdentifier, "alex"),
                    provisioning)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
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

    /// <summary>
    /// A file that renames one owner and hands their old label to another is legal, and the roster this start has
    /// written is what the second owner is judged against — a snapshot read once would refuse them for a label the
    /// first no longer carries, and the refusal would clear itself on the next start.
    /// </summary>
    [Fact]
    public async Task StartAsync_ALabelPassedFromOneDeclaredOwnerToAnother_ServesBothInOneStart()
    {
        // Arrange
        var roster = new ServedMailOwners();
        var renamed = MailOwnerId.Create(DeclaredIdentifier);

        var declared = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = DeclaredIdentifier.ToString(),
            ["Accounts:0:DisplayName"] = "sam",
            ["Accounts:1:Id"] = SyntheticMailOwner.Another.Value.ToString(),
            ["Accounts:1:DisplayName"] = "alex",
        });

        // Act
        await CreateGate(
                [Held(renamed, "alex")],
                declared,
                servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["sam", "alex"], roster.Owners.Select(owner => owner.DisplayName));
    }

    /// <summary>
    /// The same handover written the other way round — the owner taking the label declared above the one being renamed
    /// out of it. A file is judged by what it declares rather than by the order it declares it in, so the owners the
    /// deployment already holds are reconciled before the ones it does not.
    /// </summary>
    [Fact]
    public async Task StartAsync_ALabelPassedToAnOwnerDeclaredAboveTheOneLosingIt_ServesBothInOneStart()
    {
        // Arrange
        var roster = new ServedMailOwners();
        var renamed = MailOwnerId.Create(DeclaredIdentifier);

        var declared = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = SyntheticMailOwner.Another.Value.ToString(),
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:1:Id"] = DeclaredIdentifier.ToString(),
            ["Accounts:1:DisplayName"] = "sam",
        });

        // Act
        await CreateGate([Held(renamed, "alex")], declared, servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["alex", "sam"], roster.Owners.Select(owner => owner.DisplayName));
        Assert.Equal(
            [SyntheticMailOwner.Another, renamed],
            roster.Owners.Select(owner => owner.Owner));
    }

    /// <summary>
    /// The label is unique across the deployment, so an insert that wrote nothing is another owner having taken it
    /// between the roster being read and the write reaching the table. Serving the declaration anyway would hang every
    /// message of theirs on a row that is not there.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeclaredOwnerWhoseRowTheLabelKeptOut_FailsStartupNamingTheLabel()
    {
        // Arrange
        var provisioning = Substitute.For<IMailOwnerProvisioning>();

        provisioning
            .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([], Declaring(DeclaredIdentifier, "alex"), provisioning)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Free the label first", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An owner's mailboxes are declared outside the section the secret gate walks, so this is the only place their
    /// references are proven. Without it a start comes up clean and fails one connection at a time.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeclaredOwnerWhoseMailboxSecretCannotResolve_FailsStartupNamingTheOwnerAndThePath()
    {
        // Arrange
        var declared = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = DeclaredIdentifier.ToString(),
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "work",
            ["Accounts:0:MailAccounts:0:DisplayName"] = "Work",
            ["Accounts:0:MailAccounts:0:Host"] = "imap.example.test",
            ["Accounts:0:MailAccounts:0:UserName"] = "alex@example.test",
            ["Accounts:0:MailAccounts:0:Secrets:Password:Name"] = "imap-work-password",
            ["Accounts:0:MailAccounts:0:Secrets:Password:SecretReference"] = "nothing-serves-this:imap-work-password",
        });

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([], declared).StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("'alex'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Accounts:0:MailAccounts:0:Secrets:Password",
            refusal.Message,
            StringComparison.Ordinal);
    }

    /// <summary>A declaration whose secrets all resolve is served, which is what keeps the check above from refusing every owner.</summary>
    [Fact]
    public async Task StartAsync_ADeclaredOwnerWhoseMailboxSecretResolves_ServesThem()
    {
        // Arrange
        var roster = new ServedMailOwners();

        var declared = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = DeclaredIdentifier.ToString(),
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "work",
            ["Accounts:0:MailAccounts:0:DisplayName"] = "Work",
            ["Accounts:0:MailAccounts:0:Host"] = "imap.example.test",
            ["Accounts:0:MailAccounts:0:UserName"] = "alex@example.test",
            ["Accounts:0:MailAccounts:0:Secrets:Password:Name"] = "imap-work-password",
            ["Accounts:0:MailAccounts:0:Secrets:Password:SecretReference"] = "plaintext:the-mailbox-password",
        });

        // Act
        await CreateGate([], declared, servedOwners: roster).StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(["work"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>An owner the deployment holds and no file declares keeps their mail and stops being served, which is a report rather than a refusal.</summary>
    [Fact]
    public async Task StartAsync_AHeldOwnerNoFileDeclares_LeavesThemOutOfTheRosterAndNamesThemInAWarning()
    {
        // Arrange
        var roster = new ServedMailOwners();
        var startupLog = new RecordingLogger<ServedMailOwnersStartupGate>();

        // Act
        await CreateGate(
                [Held(MailOwnerId.Create(DeclaredIdentifier), "alex"), Held(SyntheticMailOwner.Another, "somebody else")],
                Declaring(DeclaredIdentifier, "alex"),
                servedOwners: roster,
                startupLog: startupLog)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);
        Assert.Equal(MailOwnerId.Create(DeclaredIdentifier), served.Owner);

        // The warning is the whole of what tells an operator that an owner's mail is kept and no longer synchronized,
        // so a report that stopped naming them would otherwise leave the state observable nowhere.
        Assert.Contains(
            startupLog.Messages,
            message => message.Contains("somebody else", StringComparison.Ordinal)
                && message.Contains("declared nowhere, so they are not served", StringComparison.Ordinal));
    }

    /// <summary>
    /// An owner-facing surface answers one person about their own mail, and nothing this release admits a caller with
    /// names the owner they act for — neither the absence of a credential nor a configured one.
    /// </summary>
    [Theory]
    [InlineData(true, "requires no authentication")]
    [InlineData(false, "whose credentials name no owner")]
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

    /// <summary>
    /// The client surface is owner-facing on exactly the terms the MCP one is, so a deployment serving several owners
    /// is refused for having it open even where no MCP endpoint is served at all.
    /// </summary>
    [Theory]
    [InlineData(true, "requires no authentication")]
    [InlineData(false, "whose credentials name no owner")]
    public async Task StartAsync_SeveralOwnersServedWithTheClientEndpointAlone_FailsStartupSayingWhy(
        bool authenticationDisabled,
        string reasonNamed)
    {
        // Arrange
        var client = new ClientEndpointOptions { Enabled = true };

        if (!authenticationDisabled)
        {
            client.Authentication.Add(new());
        }

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [],
                    TwoDeclaredOwners(),
                    clientEndpointSettings: client)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains(reasonNamed, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An administrator acts for the deployment rather than for a person, so every owner-scoped act of theirs names the
    /// owner it is for and none of them has to be resolved from the roster. The administrative surface is therefore not
    /// among the ones this refusal reads, whatever it is configured with — which is what makes recording a second owner
    /// reachable at all — so a deployment serving nobody an owner-facing surface serves several people.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralOwnersServedWithNoOwnerFacingSurfaceEnabled_ServesEveryDeclaredOwner()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        // Act
        await CreateGate([], TwoDeclaredOwners(), servedOwners: servedOwners)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, servedOwners.Owners.Count);
    }

    /// <summary>
    /// A username and password are one owner's own record, so a surface admitting nobody else can say which owner every
    /// caller acts for. It is the one credential this release has that does, and therefore the one posture under which a
    /// deployment may serve several people.
    /// </summary>
    [Fact]
    public async Task StartAsync_SeveralOwnersServedWhereEveryOwnerFacingCredentialNamesAnOwner_ServesEveryDeclaredOwner()
    {
        // Arrange
        var mcp = new McpEndpointOptions { Enabled = true };
        var servedOwners = new ServedMailOwners();

        mcp.Authentication.Add(new() { Basic = new() });

        // Act
        await CreateGate([], TwoDeclaredOwners(), servedOwners: servedOwners, mcpEndpointSettings: mcp)
            .StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, servedOwners.Owners.Count);
    }

    /// <summary>
    /// The owners a deployment holds and no file declares are kept, so a file within the bound and a table within it
    /// can still sum past it. Refusing before the writes is what keeps this start from leaving a roster every later
    /// start refuses over rows this one wrote.
    /// </summary>
    [Fact]
    public async Task StartAsync_ADeclarationThatWouldTakeTheRosterPastItsBound_FailsStartupWritingNothing()
    {
        // Arrange
        var provisioning = ProvisioningThatRecords();

        var held = Enumerable.Range(0, DeclaredOwners.MaximumDeclaredOwners)
            .Select(index => Held(MailOwnerId.Create(Guid.NewGuid()), $"held-{index}"))
            .ToArray();

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(held, Declaring(DeclaredIdentifier, "alex"), provisioning)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains(
            $"past the {DeclaredOwners.MaximumDeclaredOwners} owner records",
            refusal.Message,
            StringComparison.Ordinal);
        await provisioning.DidNotReceive()
            .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A worker switched on with no work is the deployment's own defect, and the roster is the first place every source
    /// of a mailbox is in one place: the deployment's section, an owner's declaration, and an owner's own record.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndNoServedOwnerHoldingAMailbox_FailsStartupNamingWhereToDeclareOne()
    {
        // Arrange
        var declared = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "true",
            [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.Id)}"] = DeclaredIdentifier.ToString(),
            [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.DisplayName)}"] = "alex",
        });

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([], declared, servedOwners: new ServedMailOwners()).StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("no owner this deployment serves has a mail account", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("mfctl owner account add", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case reading the files alone would refuse: both collections are empty and the mailbox this deployment exists
    /// to synchronize is in the one place a file never carries, which is the owner's own record.
    /// </summary>
    [Fact]
    public async Task StartAsync_SynchronizationOnAndTheOnlyMailboxDeclaredInAnOwnersRecord_ServesThem()
    {
        // Arrange
        var owner = MailOwnerId.Create(DeclaredIdentifier);
        var roster = new ServedMailOwners();
        var documents = Substitute.For<IOwnerSettingsDocumentReader>();

        DocumentOf(documents, owner, "adopted", "alex@example.test");

        var declared = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}"] = "true",
            [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.Id)}"] = DeclaredIdentifier.ToString(),
            [$"{DeclaredOwnerOptions.SectionName}:0:{nameof(DeclaredOwnerOptions.DisplayName)}"] = "alex",
        });

        // Act
        await CreateGate([Adopted(owner, "alex")], declared, servedOwners: roster, documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners);

        Assert.Equal(["adopted"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>A deployment that asked for nothing to be refreshed is served whatever its owners declare, including nothing.</summary>
    [Fact]
    public async Task StartAsync_SynchronizationOffAndNoMailboxAnywhere_ServesTheOwnerAnyway()
    {
        // Arrange
        var roster = new ServedMailOwners();

        // Act
        await CreateGate([], Declaring(DeclaredIdentifier, "alex"), servedOwners: roster)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Single(roster.Owners);
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
            "Secrets":{"Password":{"Name":"imap-adopted-password","SecretReference":"systemd-credential:imap-adopted-password"}}}]}
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

    /// <summary>
    /// An owner an administrator recorded is declared in no file, so nothing that reads the declarations reaches them.
    /// Leaving them unserved would be a deployment holding a row whose mail it never synchronizes and whose caller it
    /// could not compose, which is what every provisioned owner would be until somebody edited a file.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnOwnerRecordedAtRuntimeThatNoFileDeclares_ServesThemFromTheirOwnRecord()
    {
        // Arrange
        var provisioned = SyntheticMailOwner.Another;
        var roster = new ServedMailOwners();
        var documents = DocumentsHolding(
            provisioned,
            """
            {"MailAccounts":[{"AccountId":"recorded","DisplayName":"Recorded at work","Host":"imap.example.test",
            "UserName":"sam@example.test",
            "Secrets":{"Password":{"Name":"imap-recorded-password","SecretReference":"systemd-credential:imap-recorded-password"}}}]}
            """);

        // Act
        await CreateGate(
                [Held(SyntheticMailOwner.Deployment, "owner"), Adopted(provisioned, "sam")],
                servedOwners: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        var served = Assert.Single(roster.Owners, owner => owner.Owner == provisioned);
        Assert.Equal(MailOwnerAccountSource.OwnerDocument, served.Source);
        Assert.Equal(["recorded"], served.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>
    /// The roster's order is the operator's own reading of their configuration, and an owner outside it has no place in
    /// that order to take — so a recorded owner is served after the ones a file names rather than among them.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnOwnerRecordedAtRuntimeBesideADeclaredOne_ServesThemAfterTheOwnersAFileNames()
    {
        // Arrange
        var declared = MailOwnerId.Create(DeclaredIdentifier);
        var roster = new ServedMailOwners();

        // Act
        await CreateGate(
                [Adopted(SyntheticMailOwner.Another, "sam"), Held(declared, "alex")],
                Declaring(DeclaredIdentifier, "alex"),
                servedOwners: roster,
                documents: DocumentsHolding(SyntheticMailOwner.Another, "{}"))
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal([declared, SyntheticMailOwner.Another], roster.Owners.Select(owner => owner.Owner));
    }

    /// <summary>
    /// The deployment-wide naming rule a file is already held to, asked of the roster a start would serve — which is
    /// where a collision two owners wrote into their own records while a process ran first becomes visible. A write is
    /// judged against the roster this process settled, so two such writes in one run are each judged against a roster
    /// the other had not moved; this start is the first moment both records are in one place, and an account name
    /// reaching two owners resolves to whichever of them the lookup met first.
    /// </summary>
    [Fact]
    public async Task StartAsync_TwoOwnersWhoseOwnRecordsNameOneMailAccount_FailsStartupNamingTheAccount()
    {
        // Arrange
        var documents = Substitute.For<IOwnerSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailOwner.Deployment, "work", "alex@example.test");
        DocumentOf(documents, SyntheticMailOwner.Another, "work", "sam@example.test");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [Adopted(SyntheticMailOwner.Deployment, "alex"), Adopted(SyntheticMailOwner.Another, "sam")],
                    servedOwners: new ServedMailOwners(),
                    documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("work", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An owner served from the deployment's own section carries no accounts on their roster entry, so a check reading
    /// that entry alone would never see <c>MailSynchronization:Accounts</c> — and the one collision the write-time
    /// reading cannot catch, a record naming an account the section already declares, would start clean and resolve one
    /// person's mailbox for another's.
    /// </summary>
    [Fact]
    public async Task StartAsync_ARecordNamingAnAccountTheDeploymentSectionDeclares_FailsStartupNamingTheAccount()
    {
        // Arrange
        var documents = Substitute.For<IOwnerSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailOwner.Another, "work", "sam@example.test");

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate(
                    [Held(SyntheticMailOwner.Deployment, "owner"), Adopted(SyntheticMailOwner.Another, "sam")],
                    declared: DeploymentSectionDeclaring("work"),
                    servedOwners: new ServedMailOwners(),
                    documents: documents)
                .StartAsync(CancellationToken.None));

        // Assert
        Assert.Contains("work", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The same two mailboxes named apart is the ordinary case, and it starts.</summary>
    [Fact]
    public async Task StartAsync_ARecordNamingAnAccountTheDeploymentSectionDoesNot_ServesBothOwners()
    {
        // Arrange
        var roster = new ServedMailOwners();
        var documents = Substitute.For<IOwnerSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailOwner.Another, "sam-work", "sam@example.test");

        // Act
        await CreateGate(
                [Held(SyntheticMailOwner.Deployment, "owner"), Adopted(SyntheticMailOwner.Another, "sam")],
                declared: DeploymentSectionDeclaring("alex-work"),
                servedOwners: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, roster.Owners.Count);
    }

    /// <summary>The same two owners naming their mailboxes apart is the ordinary case, and it starts.</summary>
    [Fact]
    public async Task StartAsync_TwoOwnersWhoseOwnRecordsNameTheirMailAccountsApart_ServesBothOfThem()
    {
        // Arrange
        var roster = new ServedMailOwners();
        var documents = Substitute.For<IOwnerSettingsDocumentReader>();

        DocumentOf(documents, SyntheticMailOwner.Deployment, "alex-work", "alex@example.test");
        DocumentOf(documents, SyntheticMailOwner.Another, "sam-work", "sam@example.test");

        // Act
        await CreateGate(
                [Adopted(SyntheticMailOwner.Deployment, "alex"), Adopted(SyntheticMailOwner.Another, "sam")],
                servedOwners: roster,
                documents: documents)
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, roster.Owners.Count);
    }

    /// <summary>
    /// Only an owner still reading the deployment's own section contends for it, so a deployment whose every owner has
    /// adopted has no sole owner to serve — and minting one would record a person nobody asked for on every start.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoOwnerDeclaredAndEveryHeldOwnerReadingTheirOwnRecord_RecordsNobodyNew()
    {
        // Arrange
        var provisioning = ProvisioningThatRecords();
        var roster = new ServedMailOwners();

        // Act
        await CreateGate(
                [Adopted(SyntheticMailOwner.Deployment, "owner")],
                provisioning: provisioning,
                servedOwners: roster,
                documents: DocumentsHolding(SyntheticMailOwner.Deployment, "{}"))
            .StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, Assert.Single(roster.Owners).Owner);
        await provisioning.DidNotReceiveWithAnyArgs().ProvisionAsync(default, default!, CancellationToken.None);
    }

    /// <summary>
    /// Two owners reading one section is a deployment with no answer to whose mailboxes those are, and picking either
    /// would hang one person's mail on the other's row.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoOwnerDeclaredAndTwoHeldOwnersStillReadingTheSection_FailsStartup()
    {
        // Act & Assert
        await Assert.ThrowsAsync<DeploymentMailOwnerUnresolvedException>(() =>
            CreateGate([Held(SyntheticMailOwner.Deployment, "owner"), Held(SyntheticMailOwner.Another, "sam")])
                .StartAsync(CancellationToken.None));
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

    /// <summary>States one owner's own record, holding a single mail account named as the test asks.</summary>
    private static void DocumentOf(
        IOwnerSettingsDocumentReader documents,
        MailOwnerId owner,
        string accountId,
        string userName) =>
        documents.ReadAsync(owner, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<OwnerSettingsDocument?>(new OwnerSettingsDocument(
                owner,
                $"owner-{owner.Value:D}",
                $$"""
                  {
                    "MailAccounts": [
                      {
                        "AccountId": "{{accountId}}",
                        "DisplayName": "{{accountId}}",
                        "Host": "imap.example.test",
                        "UserName": "{{userName}}",
                        "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } }
                      }
                    ]
                  }
                  """,
                Version: 2,
                WrittenAtRuntime: true)));

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();

    /// <summary>The deployment's own mail section, declaring one account under the identifier a test names.</summary>
    private static IConfiguration DeploymentSectionDeclaring(string accountId) =>
        Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:AccountId"] = accountId,
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:DisplayName"] = accountId,
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:Host"] = "imap.example.test",
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:UserName"] = "alex@example.test",
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:Secrets:Password:Name"] = "imap-password",
            [$"{MailSynchronizationOptions.SectionName}:Accounts:0:Secrets:Password:SecretReference"] = "systemd-credential:imap-password",
        });

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
        IOwnerSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailOwnersStartupGate>? startupLog = null) =>
        CreateGate(
            DirectoryOf(held),
            declared,
            provisioning,
            servedOwners,
            startupGates,
            mcpEndpointSettings,
            documents,
            clientEndpointSettings,
            startupLog);

    private static ServedMailOwnersStartupGate CreateGate(
        IMailOwnerDirectory directory,
        IConfiguration? declared = null,
        IMailOwnerProvisioning? provisioning = null,
        ServedMailOwners? servedOwners = null,
        HostStartupGates? startupGates = null,
        McpEndpointOptions? mcpEndpointSettings = null,
        IOwnerSettingsDocumentReader? documents = null,
        ClientEndpointOptions? clientEndpointSettings = null,
        ILogger<ServedMailOwnersStartupGate>? startupLog = null)
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => directory);
        services.AddScoped(_ => provisioning ?? ProvisioningThatRecords());
        services.AddScoped(_ => documents ?? Substitute.For<IOwnerSettingsDocumentReader>());
        services.AddSingleton(new OwnerAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider()));
        services.AddSingleton(SecretValidator());

        return new ServedMailOwnersStartupGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            declared ?? new ConfigurationBuilder().Build(),
            servedOwners ?? new ServedMailOwners(),
            startupGates ?? new HostStartupGates(HostStartupGate.ServedMailOwners),
            new SeveralOwnerAdmission(
                Options.Create(mcpEndpointSettings ?? new McpEndpointOptions()),
                Options.Create(clientEndpointSettings ?? new ClientEndpointOptions())),
            startupLog ?? NullLogger<ServedMailOwnersStartupGate>.Instance);
    }

    /// <summary>A provisioning that reports the row as recorded, which is what every case but the contested label is.</summary>
    private static IMailOwnerProvisioning ProvisioningThatRecords() => ProvisioningThatAccepts();

    /// <summary>
    /// A provisioning standing for a database that accepts what it is given. Both of its writes are conditional and
    /// report what the row carries afterwards, so the default a substitute returns — false — is the contested label
    /// rather than the ordinary case, and a test arranging neither would be arranging a race it did not mean to.
    /// </summary>
    private static IMailOwnerProvisioning ProvisioningThatAccepts()
    {
        var provisioning = Substitute.For<IMailOwnerProvisioning>();

        provisioning
            .ProvisionAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        provisioning
            .RelabelAsync(Arg.Any<MailOwnerId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        return provisioning;
    }

    /// <summary>Resolves a reference under any scheme a deployment registers, which is what an owner's secrets are proven through here.</summary>
    /// <remarks>Composed where the record administration's tests compose it too, because the same validator judges an owner's mail accounts at a start and at a write.</remarks>
    private static SecretConfigurationValidator SecretValidator() => SecretValidation.OverRegisteredSchemes();
}
