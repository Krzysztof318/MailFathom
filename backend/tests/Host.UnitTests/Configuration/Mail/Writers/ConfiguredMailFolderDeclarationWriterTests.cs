// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail.Writers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail.Writers;

/// <summary>
/// Covers the one folder port that writes: that it writes into the account's own record under the folder grant rather
/// than the account grant, that it reads back only what that record declares, and that a rule the folder routes apply is
/// applied here too rather than gone around.
/// </summary>
public sealed class ConfiguredMailFolderDeclarationWriterTests
{
    /// <summary>The record a provisioning leaves behind, which declares nothing until its user asks for something.</summary>
    private const string EmptyRecord = "{}";
    private const string HoldingNothing = "{}";
    private const string WithholdingTheFolders = """{"MailAccounts":{"Editing":{"Mode":"AllExcept","Properties":["Folders"]}}}""";
    private const string NotAPolicy = """{"MailAccounts":{"Forced":{"Dialect":"Silesian"}}}""";

    private const string ForcingTheInbox =
        """{"MailAccounts":{"Forced":{"Folders":[{"Alias":"INBOX","RemotePath":"INBOX"}]}}}""";

    private static readonly UserId Alex = SyntheticUser.Deployment;

    /// <summary>What the surface may act on is what this record declares, and an account it does not hold declares nothing.</summary>
    [Fact]
    public async Task AliasesTheAccountDeclaresAsync_AnAccountDeclaringAFolder_ReadsItsAliases()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "PROJECTS");
        var writer = WriterOver(work, out _);

        // Act
        var declared = await writer.AliasesTheAccountDeclaresAsync(AccountOf(work), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailFolderAlias.Create("PROJECTS")], declared);
    }

    [Fact]
    public async Task AliasesTheAccountDeclaresAsync_AnAccountTheUserDoesNotHold_ReadsNone()
    {
        // Arrange
        var writer = WriterOver(Mailbox("work@example.test", "work"), out _);

        // Act
        var declared = await writer.AliasesTheAccountDeclaresAsync(
            MailAccountId.Create(Guid.NewGuid().ToString()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(declared);
    }

    /// <summary>
    /// A creation has no declared folder to be refused over, so a list a policy forces, one its editing restriction
    /// withholds, and one a policy this build does not read governs are each refused while nothing has been made yet.
    /// </summary>
    [Theory]
    [InlineData(ForcingTheInbox, null)]
    [InlineData(HoldingNothing, WithholdingTheFolders)]
    [InlineData(NotAPolicy, null)]
    public async Task FindDeclarationRefusalAsync_FoldersAPolicyHolds_RefusesACreationBeforeTheMailServerIsAsked(
        string deploymentPolicy,
        string? organizationPolicy)
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out var deployment);
        deployment.GovernedBy(Alex, StatedPolicies.Governing(deploymentPolicy, organizationPolicy));
        await using var surface = new FolderSurface(writer);

        // Act
        var outcome = await surface.Editor.CreateAsync(
            AccountOf(work),
            parentAlias: null,
            "Projects",
            role: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.FoldersHeldByPolicy, outcome.Refusal);
        Assert.Empty(surface.MailServer.ReceivedCalls());
        Assert.Null(AccountIn(deployment, work)["Folders"]);
    }

    /// <summary>The control for the refusal above: with no policy holding the list, the same creation reaches the server and is declared.</summary>
    [Fact]
    public async Task FindDeclarationRefusalAsync_FoldersNoPolicyHolds_LetsACreationReachTheMailServerAndBeDeclared()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out var deployment);
        await using var surface = new FolderSurface(writer);

        // Act
        var outcome = await surface.Editor.CreateAsync(
            AccountOf(work),
            parentAlias: null,
            "Projects",
            role: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);
        Assert.Single(surface.MailServer.ReceivedCalls());
        Assert.Equal("Projects", FolderIn(deployment, work, "0")["RemotePath"]!.GetValue<string>());
    }

    /// <summary>A client draws its menus from the read, so an act a policy withholds is not offered there to fail when asked for.</summary>
    [Theory]
    [InlineData(ForcingTheInbox, null)]
    [InlineData(HoldingNothing, WithholdingTheFolders)]
    [InlineData(NotAPolicy, null)]
    public async Task FindDeclarationRefusalAsync_FoldersAPolicyHolds_LeavesCreateOutOfTheActsTheAccountOffers(
        string deploymentPolicy,
        string? organizationPolicy)
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out var deployment);
        deployment.GovernedBy(Alex, StatedPolicies.Governing(deploymentPolicy, organizationPolicy));
        await using var surface = new FolderSurface(writer);

        // Act
        var management = await surface.Editor.ReadAsync(AccountOf(work), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(management.AllowedActs);
        Assert.Empty(management.CreatableRoles);
    }

    [Fact]
    public async Task FindDeclarationRefusalAsync_FoldersNoPolicyHolds_OffersCreate()
    {
        // Arrange
        var writer = WriterOver(Mailbox("work@example.test", "work"), out var deployment);
        var work = deployment.MailAccountRecords.Accounts.Single();
        await using var surface = new FolderSurface(writer);

        // Act
        var management = await surface.Editor.ReadAsync(AccountOf(work), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailFolderAct.Create], management.AllowedActs);
        Assert.NotEmpty(management.CreatableRoles);
    }

    /// <summary>An account the record does not name has nowhere for a declaration to be written, which is known before the server is asked too.</summary>
    [Fact]
    public async Task FindDeclarationRefusalAsync_AnAccountTheUserDoesNotHold_AnswersItAsMissing()
    {
        // Arrange
        var writer = WriterOver(Mailbox("work@example.test", "work"), out _);

        // Act
        var refusal = await writer.FindDeclarationRefusalAsync(
            MailAccountId.Create(Guid.NewGuid().ToString()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.AccountMissing, refusal);
    }

    /// <summary>The version is read here rather than supplied, because the server has already made the folder by the time this is called.</summary>
    [Fact]
    public async Task DeclareAsync_AFolderTheMailServerHasMade_DeclaresItSynchronizedWithoutBeingToldAVersion()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.DeclareAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Archive/Projects", '/'),
            role: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);

        var folder = FolderIn(deployment, work, "0");

        Assert.Equal("PROJECTS", folder["Alias"]!.GetValue<string>());
        Assert.Equal("Archive/Projects", folder["RemotePath"]!.GetValue<string>());
        Assert.True(folder["Synchronize"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RepointAsync_AFolderTheAccountDeclares_MovesThePathAndLeavesTheAliasWhereItIs()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "PROJECTS");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.RepointAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Archive/PROJECTS", '/'),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);

        var folder = FolderIn(deployment, work, "0");

        Assert.Equal("PROJECTS", folder["Alias"]!.GetValue<string>());
        Assert.Equal("Archive/PROJECTS", folder["RemotePath"]!.GetValue<string>());
    }

    [Fact]
    public async Task RepointAsync_AFolderTheAccountDoesNotDeclare_IsRefusedAsMissing()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var writer = WriterOver(work, out _);

        // Act
        var outcome = await writer.RepointAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Projects", '/'),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.FolderMissing, outcome.Refusal);
    }

    [Fact]
    public async Task WithdrawAsync_AFolderTheAccountDeclares_TakesItOutOfWhatTheAccountReads()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "PROJECTS");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.WithdrawAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(outcome.Refusal);
        Assert.False(AccountIn(deployment, work).ContainsKey("Folders"));
    }

    /// <summary>The folder an account files by is fixed wherever it is written, so this port is no way around that rule.</summary>
    [Fact]
    public async Task WithdrawAsync_AFolderPlayingARole_IsRefusedAndLeavesItDeclared()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work", "Trash", "Trash");
        var writer = WriterOver(work, out var deployment);

        // Act
        var outcome = await writer.WithdrawAsync(
            AccountOf(work),
            MailFolderAlias.Create("Trash"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.NotRecorded, outcome.Refusal);
        Assert.Equal(
            "Trash",
            AccountIn(deployment, work)["Folders"]!.AsArray()[0]!["Alias"]!.GetValue<string>());
    }

    /// <summary>The folder acts carry their own grant here, which is the one ADR 0034 allocated for them.</summary>
    [Fact]
    public async Task DeclareAsync_ACallerWithoutTheFolderGrant_IsRefused()
    {
        // Arrange
        var work = Mailbox("work@example.test", "work");
        var deployment = new UserRecordDeployment([MailFathomPermission.MailRead], Alex);
        deployment.Holding(Alex, EmptyRecord, version: 1, work);
        var writer = new ConfiguredMailFolderDeclarationWriter(deployment.MailAccounts);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => writer.DeclareAsync(
            AccountOf(work),
            MailFolderAlias.Create("PROJECTS"),
            RemoteFolderPath.Create("Projects", '/'),
            role: null,
            TestContext.Current.CancellationToken));
    }

    private static ConfiguredMailFolderDeclarationWriter WriterOver(MailAccountRecord account, out UserRecordDeployment deployment)
    {
        deployment = new UserRecordDeployment(
            [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite],
            Alex);
        deployment.Holding(Alex, EmptyRecord, version: 1, account);

        return new ConfiguredMailFolderDeclarationWriter(deployment.MailAccounts);
    }

    private static MailAccountId AccountOf(MailAccountRecord account) => MailAccountId.Create(account.Id.ToString());

    /// <summary>The mirrored folder use case over the real writer, with the mail server a substitute whose calls are countable.</summary>
    private sealed class FolderSurface : IAsyncDisposable
    {
        private readonly ClientSignals signals = new([new RecordingClientSignalChannel()], new FakeTimeProvider());

        internal FolderSurface(ConfiguredMailFolderDeclarationWriter writer)
        {
            var mappings = Substitute.For<IMailFolderMappingReader>();
            mappings.FoldersOf(Arg.Any<MailAccountId>()).Returns([]);

            this.MailServer
                .CreateFolderBeneathAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath?>(),
                    Arg.Any<string>(),
                    Arg.Any<MailFolderSpecialUse?>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => RemoteFolderPath.Create(call.ArgAt<string>(3), '/'));

            this.Editor = new MirroredMailFolderEditor(
                mappings,
                writer,
                this.MailServer,
                Substitute.For<IRemoteFolderEditor>(),
                Substitute.For<IMailTransportSecurityPolicyReader>(),
                Substitute.For<IAuthoredFolderDeleteDispositionReader>(),
                this.signals,
                Substitute.For<IJobStore>(),
                AccessAuthorizations.ForUserGranted(
                    Alex,
                    [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite]));
        }

        internal IRemoteFolderCreator MailServer { get; } = Substitute.For<IRemoteFolderCreator>();

        internal MirroredMailFolderEditor Editor { get; }

        public ValueTask DisposeAsync() => this.signals.DisposeAsync();
    }

    private static JsonObject AccountIn(UserRecordDeployment deployment, MailAccountRecord account) =>
        JsonNode.Parse(deployment.MailAccountRecords.Accounts.Single(held => held.Id == account.Id).Document)!.AsObject();

    private static JsonNode FolderIn(UserRecordDeployment deployment, MailAccountRecord account, string position) =>
        AccountIn(deployment, account)["Folders"]![position]!;

    /// <summary>A mailbox already held, optionally declaring one folder of its own.</summary>
    private static MailAccountRecord Mailbox(
        string emailAddress,
        string displayName,
        string? folderAlias = null,
        string? folderRole = null)
    {
        var secretName = emailAddress.Split('@')[0];
        var role = folderRole is null ? string.Empty : $$""", "SpecialUse": "{{folderRole}}" """;
        var folders = folderAlias is null
            ? string.Empty
            : $$"""
                ,
                "Folders": [ { "Alias": "{{folderAlias}}", "RemotePath": "{{folderAlias}}", "Synchronize": true, "CreateIfMissing": true{{role}} } ]
                """;

        return new MailAccountRecord(
            Guid.NewGuid(),
            emailAddress,
            displayName,
            $$"""
              {
                "Language": "English",
                "Host": "imap.example.test",
                "UserName": "{{emailAddress}}",
                "Secrets": { "Password": { "Name": "{{secretName}}-password", "SecretReference": "file:/run/secrets/{{secretName}}-password" } }{{folders}}
              }
              """,
            Version: 1);
    }
}
