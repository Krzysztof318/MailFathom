// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Signals;
using MailFathom.Application.Synchronization.Sessions;
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
/// Covers the folder acts a person takes on an account whose mail server is the truth: that the server is asked first
/// and a refusal leaves nothing declared, that the alias never moves, that a special folder and an operator's folder are
/// refused, that a creation may name a role instead of a name, and that both values of the deletion setting do what the
/// account said.
/// </summary>
public sealed class MirroredMailFolderEditorTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderAlias Projects = MailFolderAlias.Create("projects");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    /// <summary>The report is what a client draws its menus from, so every rule this surface applies has to be in it.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountWithARoleFolderAndAnOperatorsFolder_ReportsWhichActsEachAllows()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(
            MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')),
            MailFolderMapping.ToRemotePath(MailFolderAlias.Create("Trash"), RemoteFolderPath.Create("Trash", '/'), specialUse: MailFolderSpecialUse.Trash),
            MailFolderMapping.ToRemotePath(MailFolderAlias.Create("shared"), RemoteFolderPath.Create("Shared", '/')));
        deployment.Declarations.Declared.Add(Projects);

        // Act
        var management = await deployment.Editor.ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailFolderAct.Create], management.AllowedActs);
        Assert.Equal(
            [MailFolderSpecialUse.Archive, MailFolderSpecialUse.Drafts, MailFolderSpecialUse.Sent, MailFolderSpecialUse.Junk],
            management.CreatableRoles);
        Assert.Equal(
            [
                ["Rename", "Move", "Delete"],
                [],
                [],
            ],
            management.Folders.Select(folder => folder.AllowedActs.Select(act => act.ToString())));
        Assert.Equal(["Projects", "Shared", "Trash"], management.Folders.Select(folder => folder.Name));
    }

    [Fact]
    public async Task CreateAsync_AnOrdinaryFolder_CreatesItOnTheServerAndThenDeclaresIt()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("Archive", '/')));
        deployment.CreatesAt("Projects");

        // Act
        var outcome = await deployment.Editor.CreateAsync(Account, parentAlias: null, "Projects", role: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Created, outcome.Change);
        Assert.Equal("PROJECTS", outcome.Folder!.Id);
        Assert.Equal(["created Projects", "declared PROJECTS at Projects"], deployment.Log);
    }

    /// <summary>A creation naming a role names no name: the standard English name is the service's to supply.</summary>
    [Fact]
    public async Task CreateAsync_ARoleTheAccountHasNoFolderFor_CreatesItUnderTheRolesStandardName()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.CreatesAt("Archive");

        // Act
        var outcome = await deployment.Editor.CreateAsync(
            Account,
            parentAlias: null,
            name: null,
            MailFolderSpecialUse.Archive,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderSpecialUse.Archive, outcome.Folder!.Role);
        Assert.Equal("Archive", outcome.Folder.Name);
        Assert.Equal(["created Archive", "declared ARCHIVE at Archive"], deployment.Log);
    }

    /// <summary>
    /// The name RFC 3501 reserves belongs to the one folder every account already has, and that rule is MailFathom's
    /// own rather than whatever a source server would have answered.
    /// </summary>
    [Fact]
    public async Task CreateAsync_TheReservedNameAtTheTopOfTheHierarchy_IsRefusedAndReachesNoServer()
    {
        // Arrange
        await using var deployment = new EditorDeployment();

        // Act
        var outcome = await deployment.Editor.CreateAsync(Account, parentAlias: null, "inbox", role: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.InboxNameAtTopLevel, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    [Fact]
    public async Task RenameAsync_ATopLevelFolderToTheReservedName_IsRefusedAndReachesNoServer()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));
        deployment.Declarations.Declared.Add(Projects);

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, Projects, "INBOX", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.InboxNameAtTopLevel, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    /// <summary>
    /// The window between the server acting and MailFathom writing it down is the one place a cancelled act would leave
    /// the two sides disagreeing, so the declaration write is given a token that cannot be cancelled.
    /// </summary>
    [Fact]
    public async Task CreateAsync_TheServerHasAlreadyActed_WritesTheDeclarationWithoutTheCallersCancellation()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        using var cancellation = new CancellationTokenSource();
        deployment.CreatesAt("Projects");

        // Act
        await deployment.Editor.CreateAsync(Account, parentAlias: null, "Projects", role: null, cancellation.Token);

        // Assert
        Assert.All(deployment.Declarations.WriteTokens, token => Assert.False(token.CanBeCanceled));
        Assert.NotEmpty(deployment.Declarations.WriteTokens);
    }

    [Fact]
    public async Task CreateAsync_ARoleTheAccountAlreadyHasAFolderFor_IsRefusedAndReachesNoServer()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToSpecialUse(Archive, MailFolderSpecialUse.Archive));

        // Act
        var outcome = await deployment.Editor.CreateAsync(
            Account,
            parentAlias: null,
            name: null,
            MailFolderSpecialUse.Archive,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.RoleAlreadyPlayed, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    /// <summary>
    /// An ordinary folder first created as <c>Trash</c> keeps that alias through every later rename, so the role's own
    /// alias is taken and a second declaration under it would strand one of the two folders with its mail.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ARoleWhoseAliasAnOrdinaryFolderHolds_IsRefusedAndIsNotOffered()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("Trash"),
            RemoteFolderPath.Create("Discarded", '/')));

        // Act
        var outcome = await deployment.Editor.CreateAsync(
            Account,
            parentAlias: null,
            name: null,
            MailFolderSpecialUse.Trash,
            TestContext.Current.CancellationToken);
        var management = await deployment.Editor.ReadAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.RoleAlreadyPlayed, outcome.Refusal);
        Assert.DoesNotContain(MailFolderSpecialUse.Trash, management.CreatableRoles);
        Assert.Empty(deployment.Log);
    }

    /// <summary>The alias is what the stored mail and the checkpoints are keyed by, so a rename moves the path and nothing else.</summary>
    [Fact]
    public async Task RenameAsync_AFolderTheAccountDeclares_RenamesItOnTheServerAndLeavesTheAliasWhereItIs()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));
        deployment.Declarations.Declared.Add(Projects);
        deployment.RenamesTo("Plans");

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, Projects, "Plans", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Renamed, outcome.Change);
        Assert.Equal("PROJECTS", outcome.Folder!.Id);
        Assert.Equal("Plans", outcome.Folder.Name);
        Assert.Equal(["renamed PROJECTS to Plans", "repointed PROJECTS at Plans"], deployment.Log);
    }

    [Fact]
    public async Task MoveAsync_BeneathAFolderTheAccountDeclares_MovesItWithTheOneRename()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(
            MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')),
            MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("Archive", '/')));
        deployment.Declarations.Declared.Add(Projects);
        deployment.RenamesTo("Archive/Projects");

        // Act
        var outcome = await deployment.Editor.MoveAsync(Account, Projects, Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Moved, outcome.Change);
        Assert.Equal("ARCHIVE", outcome.Folder!.ParentId);
        Assert.Equal(["renamed PROJECTS to Projects", "repointed PROJECTS at Archive/Projects"], deployment.Log);
    }

    [Fact]
    public async Task MoveAsync_BeneathItself_IsRefusedAndReachesNoServer()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(
            MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')),
            MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("Projects/Archive", '/')));
        deployment.Declarations.Declared.Add(Projects);

        // Act
        var outcome = await deployment.Editor.MoveAsync(Account, Projects, Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.NestedInItself, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    /// <summary>
    /// A server delimited by something other than a slash may advertise a folder whose own name carries one, and a move
    /// keeps that name. It is refused as the invalid name it is rather than thrown out of the act.
    /// </summary>
    [Fact]
    public async Task MoveAsync_AFolderTheServerNamesWithAHierarchyDelimiter_IsRefusedAndReachesNoServer()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(
            MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Team/Notes", '.')),
            MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("Archive", '.')));
        deployment.Declarations.Declared.Add(Projects);

        // Act
        var outcome = await deployment.Editor.MoveAsync(Account, Projects, Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.NameInvalid, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    /// <summary>Deleting on the server takes the folder away on both sides, and the mail stored from it goes with it.</summary>
    [Fact]
    public async Task DeleteAsync_TheAccountDeletesOnTheServer_DeletesItThereWithdrawsItAndQueuesItsMail()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));
        deployment.Declarations.Declared.Add(Projects);

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, Projects, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.Deleted, outcome.Change);
        Assert.Equal(["deleted PROJECTS", "withdrew PROJECTS"], deployment.Log);
        await deployment.Jobs.Received(1).EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Marking deleted reaches the server not at all, and the folder's mail stays stored against the day it is declared again.</summary>
    [Fact]
    public async Task DeleteAsync_TheAccountMarksDeletedOnly_WithdrawsItWithoutReachingTheServerOrTheMail()
    {
        // Arrange
        await using var deployment = new EditorDeployment(AuthoredFolderDeleteDisposition.MarkDeletedLocally);
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));
        deployment.Declarations.Declared.Add(Projects);

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, Projects, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderChangeKind.MarkedDeleted, outcome.Change);
        Assert.Equal(["withdrew PROJECTS"], deployment.Log);
        await deployment.Jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The whole of the never-half-an-act rule: the server answers first, so a refusal leaves nothing declared.</summary>
    [Fact]
    public async Task DeleteAsync_ServerRefusesTheDeletion_IsReportedAsARefusalAndWithdrawsNothing()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));
        deployment.Declarations.Declared.Add(Projects);
        deployment.RemoteEditor
            .DeleteFolderAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderAlias>(),
                Arg.Any<RemoteFolderPath>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new RemoteFolderEditRefusedException(Account, Projects, MailFolderAct.Delete));

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, Projects, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.ServerRefused, outcome.Refusal);
        Assert.Contains(Projects, deployment.Declarations.Declared);
        Assert.Empty(deployment.Channel.Published);
    }

    [Fact]
    public async Task RenameAsync_ServerDoesNotAnswer_IsReportedAsUnavailableAndRepointsNothing()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));
        deployment.Declarations.Declared.Add(Projects);
        deployment.RemoteEditor
            .RenameFolderAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderAlias>(),
                Arg.Any<RemoteFolderPath>(),
                Arg.Any<RemoteFolderPath?>(),
                Arg.Any<string>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns<RemoteFolderPath>(_ => throw new MailboxUnavailableException(Account, new TimeoutException()));

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, Projects, "Plans", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.ServerUnavailable, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    /// <summary>The folder an account files by is not one an act may take away, which holds for all three acts that name a folder.</summary>
    [Fact]
    public async Task DeleteAsync_AFolderPlayingARole_IsRefusedAsProtected()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("Trash"),
            RemoteFolderPath.Create("Trash", '/'),
            specialUse: MailFolderSpecialUse.Trash));
        deployment.Declarations.Declared.Add(MailFolderAlias.Create("Trash"));

        // Act
        var outcome = await deployment.Editor.DeleteAsync(Account, MailFolderAlias.Create("Trash"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.ProtectedRole, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    /// <summary>What an operator wrote is theirs, so a folder the deployment's own configuration declares is refused here.</summary>
    [Fact]
    public async Task RenameAsync_AFolderTheDeploymentsOwnConfigurationDeclares_IsRefused()
    {
        // Arrange
        await using var deployment = new EditorDeployment();
        deployment.Declares(MailFolderMapping.ToRemotePath(Projects, RemoteFolderPath.Create("Projects", '/')));

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, Projects, "Plans", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.NotDeclaredByTheAccount, outcome.Refusal);
        Assert.Empty(deployment.Log);
    }

    [Fact]
    public async Task RenameAsync_AFolderTheAccountDoesNotHave_IsRefusedAsMissing()
    {
        // Arrange
        await using var deployment = new EditorDeployment();

        // Act
        var outcome = await deployment.Editor.RenameAsync(Account, Projects, "Plans", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFolderActRefusal.FolderMissing, outcome.Refusal);
    }

    [Fact]
    public async Task RenameAsync_ACallerWithoutTheFolderGrant_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        await using var deployment = new EditorDeployment(granted: MailFathomPermission.MailRead);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => deployment.Editor.RenameAsync(Account, Projects, "Plans", TestContext.Current.CancellationToken));
    }

    /// <summary>The use case over substituted ports, with every act it takes written into one log so its order is assertable.</summary>
    private sealed class EditorDeployment : IAsyncDisposable
    {
        private readonly IMailFolderMappingReader mappings = Substitute.For<IMailFolderMappingReader>();

        internal EditorDeployment(
            AuthoredFolderDeleteDisposition disposition = AuthoredFolderDeleteDisposition.DeleteOnServer,
            params MailFathomPermission[] granted)
        {
            this.Signals = new ClientSignals([this.Channel], new FakeTimeProvider());
            this.Declarations = new RecordingDeclarationWriter(this.Log);
            this.mappings.FoldersOf(Arg.Any<MailAccountId>()).Returns(_ => this.folders);

            var transportPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

            var dispositions = Substitute.For<IAuthoredFolderDeleteDispositionReader>();
            dispositions.GetAuthoredFolderDeleteDisposition(Arg.Any<MailAccountId>()).Returns(disposition);

            this.RemoteEditor
                .When(editor => editor.DeleteFolderAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>()))
                .Do(call => this.Log.Add($"deleted {call.ArgAt<MailFolderAlias>(1).Value}"));

            this.Editor = new MirroredMailFolderEditor(
                this.mappings,
                this.Declarations,
                this.RemoteCreator,
                this.RemoteEditor,
                transportPolicies,
                dispositions,
                this.Signals,
                this.Jobs,
                AccessAuthorizations.ForUserGranted(
                    SyntheticMailUser.Deployment,
                    granted.Length > 0 ? granted : [MailFathomPermission.MailRead, MailFathomPermission.MailFoldersWrite]));
        }

        internal List<string> Log { get; } = [];

        internal RecordingDeclarationWriter Declarations { get; }

        internal IRemoteFolderCreator RemoteCreator { get; } = Substitute.For<IRemoteFolderCreator>();

        internal IRemoteFolderEditor RemoteEditor { get; } = Substitute.For<IRemoteFolderEditor>();

        internal IJobStore Jobs { get; } = Substitute.For<IJobStore>();

        internal RecordingClientSignalChannel Channel { get; } = new();

        internal ClientSignals Signals { get; }

        internal MirroredMailFolderEditor Editor { get; }

        private IReadOnlyList<MailFolderMapping> folders = [];

        /// <summary>States what the whole of configuration declares for the account.</summary>
        internal void Declares(params MailFolderMapping[] declared) => this.folders = declared;

        /// <summary>Scripts the mail server answering a creation with the path it advertises the folder at.</summary>
        internal void CreatesAt(string advertisedPath) =>
            this.RemoteCreator
                .CreateFolderBeneathAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath?>(),
                    Arg.Any<string>(),
                    Arg.Any<MailFolderSpecialUse?>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.Log.Add($"created {call.ArgAt<string>(3)}");

                    return RemoteFolderPath.Create(advertisedPath, '/');
                });

        /// <summary>Scripts the mail server answering a rename with the path it advertises the folder at afterwards.</summary>
        internal void RenamesTo(string advertisedPath) =>
            this.RemoteEditor
                .RenameFolderAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<RemoteFolderPath?>(),
                    Arg.Any<string>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.Log.Add($"renamed {call.ArgAt<MailFolderAlias>(1).Value} to {call.ArgAt<string>(4)}");

                    return RemoteFolderPath.Create(advertisedPath, '/');
                });

        public ValueTask DisposeAsync() => this.Signals.DisposeAsync();
    }

    /// <summary>The account's own declarations, holding what it declares and writing every change into the shared log.</summary>
    private sealed class RecordingDeclarationWriter(List<string> log) : IMailFolderDeclarationWriter
    {
        internal HashSet<MailFolderAlias> Declared { get; } = [];

        /// <summary>The token each write was given, which is how a test reads whether the write could have been cancelled.</summary>
        internal List<CancellationToken> WriteTokens { get; } = [];

        public Task<IReadOnlySet<MailFolderAlias>> AliasesTheAccountDeclaresAsync(
            MailAccountId accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<MailFolderAlias>>(this.Declared);

        public Task<MailFolderDeclarationOutcome> DeclareAsync(
            MailAccountId accountId,
            MailFolderAlias folderAlias,
            RemoteFolderPath path,
            MailFolderSpecialUse? role,
            CancellationToken cancellationToken)
        {
            log.Add($"declared {folderAlias.Value} at {path.Value}");
            this.WriteTokens.Add(cancellationToken);
            this.Declared.Add(folderAlias);

            return Task.FromResult(MailFolderDeclarationOutcome.Committed);
        }

        public Task<MailFolderDeclarationOutcome> RepointAsync(
            MailAccountId accountId,
            MailFolderAlias folderAlias,
            RemoteFolderPath path,
            CancellationToken cancellationToken)
        {
            log.Add($"repointed {folderAlias.Value} at {path.Value}");
            this.WriteTokens.Add(cancellationToken);

            return Task.FromResult(MailFolderDeclarationOutcome.Committed);
        }

        public Task<MailFolderDeclarationOutcome> WithdrawAsync(
            MailAccountId accountId,
            MailFolderAlias folderAlias,
            CancellationToken cancellationToken)
        {
            log.Add($"withdrew {folderAlias.Value}");
            this.WriteTokens.Add(cancellationToken);
            this.Declared.Remove(folderAlias);

            return Task.FromResult(MailFolderDeclarationOutcome.Committed);
        }
    }
}
