// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders;

/// <summary>
/// Covers the one folder-management use case: that the account's phase alone decides which editor carries an act, that
/// nothing it answers says where the mail is kept, that an account being restored allows nothing, and that a folder
/// named by a value this deployment never issued is a missing folder rather than a malformed request.
/// </summary>
public sealed class MailFolderEditorTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("primary"));

    private static readonly MailFolderAlias Projects = MailFolderAlias.Create("projects");

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    /// <summary>A held account's folders are rows, and the report names them by an identity whose shape is not the contract's.</summary>
    [Fact]
    public async Task ReadAsync_AHeldAccount_ReportsItsOwnFoldersAndWhichActsTheyAllow()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var management = await deployment.Editor.ReadAsync(Account.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailFolderAct.Create], management!.AllowedActs);
        Assert.Empty(management.CreatableRoles);
        Assert.Equal(["Drafts", "INBOX", "Junk", "Sent", "Trash"], management.Folders.Select(folder => folder.Name));
        Assert.All(management.Folders, folder => Assert.Empty(folder.AllowedActs));
    }

    [Fact]
    public async Task ReadAsync_AMirroredAccount_ReportsTheFoldersItsMailServerHolds()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Mirrored);
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));

        // Act
        var management = await deployment.Editor.ReadAsync(Account.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Projects"], management!.Folders.Select(folder => folder.Name));
    }

    [Fact]
    public async Task ReadAsync_AnAccountTheCallerDoesNotHold_AnswersNothing()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var management = await deployment.Editor.ReadAsync(MailAccountId.Create("other"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(management);
    }

    [Fact]
    public async Task CreateAsync_AHeldAccount_CreatesTheFolderInTheLocalHierarchy()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var outcome = await deployment.Editor.CreateAsync(Account.Id, parentId: null, "Projects", role: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Created, outcome.Change);
        Assert.Equal("Projects", outcome.Folder!.Name);
        Assert.Contains("Projects", deployment.Store.Folders.Select(folder => folder.Name.Value));
    }

    /// <summary>A held account is given every role it needs the moment its hierarchy is read, so no role is ever waiting to be created.</summary>
    [Fact]
    public async Task CreateAsync_AHeldAccountAndARole_IsRefusedBecauseTheRoleIsAlreadyPlayed()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var outcome = await deployment.Editor.CreateAsync(
            Account.Id,
            parentId: null,
            name: null,
            MailFolderSpecialUse.Archive,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.RoleAlreadyPlayed, outcome.Refusal);
    }

    /// <summary>
    /// Absence of a parent is what names the top of the hierarchy, so text that names no folder must not be read as it:
    /// the folder would be created somewhere the request never asked for.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AHeldAccountAndAParentThisDeploymentNeverIssued_IsRefusedAsAMissingParent()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);

        // Act
        var outcome = await deployment.Editor.CreateAsync(
            Account.Id,
            "not-an-identifier",
            "Projects",
            role: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.ParentMissing, outcome.Refusal);
        Assert.DoesNotContain("Projects", deployment.Store.Folders.Select(folder => folder.Name.Value));
    }

    [Fact]
    public async Task MoveAsync_AHeldAccountAndAParentThisDeploymentNeverIssued_IsRefusedAsAMissingParent()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held);
        var created = await deployment.Editor.CreateAsync(Account.Id, parentId: null, "Projects", role: null, TestContext.Current.CancellationToken);

        // Act
        var outcome = await deployment.Editor.MoveAsync(
            Account.Id,
            created.Folder!.Id,
            "not-an-identifier",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.ParentMissing, outcome.Refusal);
    }

    [Fact]
    public async Task CreateAsync_AMirroredAccount_CreatesTheFolderOnItsMailServer()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Mirrored);
        deployment.CreatesAt("Projects");

        // Act
        var outcome = await deployment.Editor.CreateAsync(Account.Id, parentId: null, "Projects", role: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Created, outcome.Change);
        Assert.Equal("PROJECTS", outcome.Folder!.Id);
    }

    /// <summary>An account on its way back to its source is half in each place, so an act on either side would be an act against half a mailbox.</summary>
    [Fact]
    public async Task DeleteAsync_AnAccountBeingRestored_IsRefused()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Restoring);

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account.Id, Guid.CreateVersion7().ToString(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.AccountNotHeld, outcome.Refusal);
    }

    /// <summary>A caller can only have got an identity from this surface, so text that is neither shape is a folder the account does not have.</summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Held)]
    [InlineData(MailAccountCustodyPhase.Mirrored)]
    public async Task RenameAsync_AFolderNamedByAValueThisDeploymentNeverIssued_IsRefusedAsMissing(MailAccountCustodyPhase phase)
    {
        // Arrange
        await using var deployment = new EditorDeployment(phase);

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account.Id, "   ", "Plans", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.FolderMissing, outcome.Refusal);
    }

    [Fact]
    public async Task CreateAsync_ACallerWithoutTheFolderGrant_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        await using var deployment = new EditorDeployment(MailAccountCustodyPhase.Held, MailFathomPermission.MailRead);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => deployment.Editor.CreateAsync(Account.Id, parentId: null, "Projects", role: null, TestContext.Current.CancellationToken));
    }

    /// <summary>The use case over both editors, each composed over the doubles its own suite uses.</summary>
    private sealed class EditorDeployment : IAsyncDisposable
    {
        private readonly IMailFolderMappingReader mappings = Substitute.For<IMailFolderMappingReader>();
        private readonly IRemoteFolderCreator creator = Substitute.For<IRemoteFolderCreator>();
        private readonly IMailFolderDeclarationWriter declarations = Substitute.For<IMailFolderDeclarationWriter>();
        private IReadOnlyList<MailFolderMapping> folders = [];

        internal EditorDeployment(MailAccountCustodyPhase phase, params MailFathomPermission[] granted)
        {
            this.Store = new InMemoryLocalMailFolderStore(Account, phase);
            this.Signals = new ClientSignals([new RecordingClientSignalChannel()], this.Clock);
            this.mappings.FoldersOf(Arg.Any<MailAccountId>()).Returns(_ => this.folders);
            this.declarations
                .AliasesTheAccountDeclaresAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlySet<MailFolderAlias>>(new HashSet<MailFolderAlias>()));
            this.declarations
                .DeclareAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<MailFolderSpecialUse?>(),
                    Arg.Any<CancellationToken>())
                .Returns(MailFolderDeclarationOutcome.Committed);

            var transportPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

            var dispositions = Substitute.For<IAuthoredFolderDeleteDispositionReader>();
            dispositions
                .GetAuthoredFolderDeleteDisposition(Arg.Any<MailAccountId>())
                .Returns(AuthoredFolderDeleteDisposition.DeleteOnServer);

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

            var authorization = AccessAuthorizations.ForUserGranted(
                SyntheticMailUser.Deployment,
                granted.Length > 0 ? granted : [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite]);

            this.Editor = new MailFolderEditor(
                new LocalMailFolderEditor(
                    this.Store,
                    new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.Clock),
                    Substitute.For<ILocalMailFolderChangeAuditor>(),
                    this.Signals,
                    Substitute.For<IJobStore>(),
                    authorization,
                    this.Clock),
                new MirroredMailFolderEditor(
                    this.mappings,
                    this.declarations,
                    this.creator,
                    Substitute.For<IRemoteFolderEditor>(),
                    transportPolicies,
                    dispositions,
                    this.Signals,
                    Substitute.For<IJobStore>(),
                    authorization),
                authorization);
        }

        internal FakeTimeProvider Clock { get; } = new();

        internal InMemoryLocalMailFolderStore Store { get; }

        internal ClientSignals Signals { get; }

        internal MailFolderEditor Editor { get; }

        /// <summary>States what the whole of configuration declares for a mirrored account.</summary>
        internal void Declares(params MailFolderMapping[] declared) => this.folders = declared;

        /// <summary>Scripts the mail server answering a creation with the path it advertises the folder at.</summary>
        internal void CreatesAt(string advertisedPath) =>
            this.creator
                .CreateFolderBeneathAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath?>(),
                    Arg.Any<string>(),
                    Arg.Any<MailFolderSpecialUse?>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(RemoteFolderPath.Create(advertisedPath, '/'));

        public ValueTask DisposeAsync() => this.Signals.DisposeAsync();
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
