// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders;

public sealed class MailFolderResolverTests
{
    private static readonly MailAccountIdentity PrimaryAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("primary"));

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    [Fact]
    public async Task ResolveAsync_MappingNamesAnAdvertisedPath_BindsTheAliasToItsFirstGeneration()
    {
        // Arrange
        var advertisedArchive = RemoteFolderPath.Create("Archief/2026", '/');
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]),
            new RemoteFolder(advertisedArchive, []));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archief/2026"));

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailFolderResolutionOutcome.Resolved, result.Outcome);
        Assert.Equal("ARCHIVE", result.Resolution!.Alias.Value);
        Assert.Equal(1, result.Resolution.Generation.Value);
        Assert.Equal(advertisedArchive, result.Resolution.RemotePath);
    }

    /// <summary>An operator who never learns their server's word for "inbox" is the point of a special-use mapping.</summary>
    [Fact]
    public async Task ResolveAsync_MappingNamesTheInboxRole_BindsTheFolderTheServerReportsItFor()
    {
        // Arrange
        var localizedInbox = RemoteFolderPath.Create("Skrzynka odbiorcza", '/');
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("Archief", '/'), []),
            new RemoteFolder(localizedInbox, [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(localizedInbox, result.Resolution!.RemotePath);
    }

    /// <summary>RFC 3501 guarantees the name INBOX even when RFC 6154 attributes are missing entirely.</summary>
    [Fact]
    public async Task ResolveAsync_ServerReportsNoSpecialUseAttributes_StillBindsTheInboxByItsMandatedName()
    {
        // Arrange
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("Archief", '/'), []),
            new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), []));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal("INBOX", result.Resolution!.RemotePath.Value);
    }

    /// <summary>Only the inbox has a name the protocol mandates, so no other role may be guessed at by name.</summary>
    [Fact]
    public async Task ResolveAsync_ServerReportsNoAttributeForANonInboxRole_ReportsNoAdvertisedFolderMatched()
    {
        // Arrange
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("Archive", '/'), []),
            new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), []));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("archive"), MailFolderSpecialUse.Archive);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailFolderResolutionOutcome.NoAdvertisedFolderMatched, result.Outcome);
        Assert.Null(result.Resolution);
    }

    /// <summary>
    /// IMAP LIST ordering is a response order, not an identity contract. Taking the first of several folders carrying
    /// a role would let a reordered response repoint the alias, start a generation, and resynchronize a different
    /// folder with no configuration having changed.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_SeveralAdvertisedFoldersCarryTheRole_ReportsAmbiguityInsteadOfChoosingByResponseOrder()
    {
        // Arrange
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("Archief", '/'), [MailFolderSpecialUse.Archive]),
            new RemoteFolder(RemoteFolderPath.Create("Archive", '/'), [MailFolderSpecialUse.Archive]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("archive"), MailFolderSpecialUse.Archive);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailFolderResolutionOutcome.AdvertisedFoldersAreAmbiguous, result.Outcome);
        Assert.Null(result.Resolution);
        await context.PersistenceSessionFactory.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Naming the path is the remedy for an ambiguous role, so it must still resolve one folder.</summary>
    [Fact]
    public async Task ResolveAsync_ExplicitPathAmongFoldersSharingARole_ResolvesTheNamedFolder()
    {
        // Arrange
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("Archief", '/'), [MailFolderSpecialUse.Archive]),
            new RemoteFolder(RemoteFolderPath.Create("Archive", '/'), [MailFolderSpecialUse.Archive]));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive"));

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal("Archive", result.Resolution!.RemotePath.Value);
    }

    [Fact]
    public async Task ResolveAsync_AliasMatchesNothingAdvertised_LeavesTheOtherAliasesOfTheAccountResolvable()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var missingMapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archief"));
        var inboxMapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act
        var missingResult = await context.Resolver.ResolveAsync(PrimaryAccount, missingMapping, RequiredTlsPolicy, CancellationToken.None);
        var inboxResult = await context.Resolver.ResolveAsync(PrimaryAccount, inboxMapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailFolderResolutionOutcome.NoAdvertisedFolderMatched, missingResult.Outcome);
        Assert.Equal(MailFolderResolutionOutcome.Resolved, inboxResult.Outcome);
        await context.ResolutionStore.DidNotReceive().SaveResolutionAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailAccountIdentity>(),
            Arg.Is<MailFolderResolution>(resolution => resolution!.Alias.Value == "ARCHIVE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_AliasAlreadyBoundToTheAdvertisedFolder_WritesNothingAndRecordsNoChange()
    {
        // Arrange
        var advertisedInbox = RemoteFolderPath.Create("INBOX", '/');
        await using var context = new ResolverContext(new RemoteFolder(advertisedInbox, [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);
        context.BindAliasTo(MailFolderResolution.FirstBindingOf(mapping.Alias, advertisedInbox));

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Resolution!.Generation.Value);
        await context.PersistenceSessionFactory.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
        await context.MappingChangeAuditor.DidNotReceive().RecordMappingChangeAsync(Arg.Any<MailFolderMappingChange>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_FirstBindingOfAnAlias_RecordsItWithNoPreviousRemotePath()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act
        await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        var change = Assert.Single(context.RecordedChanges);
        Assert.Null(change.PreviousRemotePath);
        Assert.Equal("INBOX", change.NewRemotePath.Value);
        Assert.Equal(1, change.Generation.Value);
        Assert.Equal(ResolverContext.ResolvedAt, change.OccurredAt);
    }

    [Fact]
    public async Task ResolveAsync_AliasRepointed_RecordsBothRemotePathsAndTheNewGeneration()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("Archive/2026", '/'), []));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive/2026"));
        context.BindAliasTo(MailFolderResolution.FirstBindingOf(mapping.Alias, RemoteFolderPath.Create("Archief", '/')));

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        var change = Assert.Single(context.RecordedChanges);
        Assert.Equal("Archief", change.PreviousRemotePath!.Value.Value);
        Assert.Equal("Archive/2026", change.NewRemotePath.Value);
        Assert.Equal(2, change.Generation.Value);
        Assert.Equal(2, result.Resolution!.Generation.Value);
    }

    /// <summary>
    /// The failure this design exists to prevent. Two unrelated remote folders may advertise the same UIDVALIDITY,
    /// because the value is unique inside one mailbox and means nothing across mailboxes. A repointed alias that kept
    /// one identity would let the previous folder's checkpoint apply to the new folder and skip every message below
    /// its last-seen UID — silently, permanently, and without any failure to notice.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AliasRepointedToAFolderWithTheSameUidValidity_StartsAGenerationThatHasNoCheckpoint()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("Archive/2026", '/'), []));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive/2026"));
        var previousBinding = MailFolderResolution.FirstBindingOf(mapping.Alias, RemoteFolderPath.Create("Archief", '/'));
        context.BindAliasTo(previousBinding);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.NotEqual(previousBinding.Id, result.Resolution!.Id);
        Assert.Equal(previousBinding.Alias, result.Resolution.Alias);
        await context.ResolutionStore.Received(1).SaveResolutionAsync(
            Arg.Any<IPersistenceSession>(),
            PrimaryAccount,
            Arg.Is<MailFolderResolution>(resolution => resolution!.Generation.Value == 2),
            CancellationToken.None);
    }

    [Fact]
    public async Task ResolveAsync_CompetingWriterRecordedTheBindingFirst_ReportsTheConcurrencyConflict()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        context.PersistenceSession
            .CommitAsync(Arg.Any<CancellationToken>())
            .Returns(PersistenceCommitResult.ConcurrencyConflict);
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act, Assert
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None));
        await context.MappingChangeAuditor.DidNotReceive().RecordMappingChangeAsync(Arg.Any<MailFolderMappingChange>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The two halves of the reopened decision, asserted side by side so the refusal cannot quietly become a creation.
    /// A mapping that says nothing about creation keeps reporting the unresolved alias a mistyped path produces, and
    /// only the mapping that asked for it reaches the mail server at all.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_NothingAdvertisedAtTheConfiguredPath_CreatesTheFolderOnlyForTheMappingThatAskedFor()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var silentMapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archief"),
            RemoteFolderPath.Create("Archief"));
        var creatingMapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive/2026"),
            participation: null,
            mayCreateMissingFolder: true);

        // Act
        var silentResult = await context.Resolver.ResolveAsync(PrimaryAccount, silentMapping, RequiredTlsPolicy, CancellationToken.None);
        var creatingResult = await context.Resolver.ResolveAsync(PrimaryAccount, creatingMapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailFolderResolutionOutcome.NoAdvertisedFolderMatched, silentResult.Outcome);
        Assert.Equal(MailFolderResolutionOutcome.Resolved, creatingResult.Outcome);
        Assert.Equal(["Archive/2026"], context.CreatedPaths.Select(path => path.Value));
    }

    /// <summary>A created folder is bound exactly as a discovered one, which is what keeps everything downstream of resolution indifferent to how the folder came to exist.</summary>
    [Fact]
    public async Task ResolveAsync_FolderWasCreated_BindsItAndAuditsTheChangeAsAnOrdinaryFirstBinding()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive/2026"),
            participation: null,
            mayCreateMissingFolder: true);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(RemoteFolderPath.Create("Archive/2026", '/'), result.Resolution!.RemotePath);
        Assert.Equal(1, result.Resolution.Generation.Value);

        var change = Assert.Single(context.RecordedChanges);
        Assert.Null(change.PreviousRemotePath);
        Assert.Equal("Archive/2026", change.NewRemotePath.Value);
        Assert.Equal(ResolverContext.ResolvedAt, change.OccurredAt);
    }

    /// <summary>
    /// The created folder binds under the delimiter the server reports rather than the configured spelling, which is
    /// what stops the run after the creating one from reading its own binding as a repointed alias and starting a
    /// second generation over a folder nothing changed.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RunAfterTheOneThatCreatedTheFolder_ReturnsTheSameBindingWithoutWritingAgain()
    {
        // Arrange
        var createdPath = RemoteFolderPath.Create("Archive/2026", '/');
        await using var context = new ResolverContext(new RemoteFolder(createdPath, []));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive/2026"),
            participation: null,
            mayCreateMissingFolder: true);
        context.BindAliasTo(MailFolderResolution.FirstBindingOf(mapping.Alias, createdPath));

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Resolution!.Generation.Value);
        Assert.Empty(context.CreatedPaths);
        await context.MappingChangeAuditor.DidNotReceive().RecordMappingChangeAsync(Arg.Any<MailFolderMappingChange>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A folder that does not exist advertises no role, so a role mapping can never be the thing that creates one.</summary>
    [Fact]
    public async Task ResolveAsync_RoleMappingMatchesNothingAdvertised_ReachesNoCreationAtAll()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("archive"), MailFolderSpecialUse.Archive);

        // Act
        var result = await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        // Assert
        Assert.Equal(MailFolderResolutionOutcome.NoAdvertisedFolderMatched, result.Outcome);
        Assert.Empty(context.CreatedPaths);
    }

    /// <summary>A refused creation stays what it is rather than becoming the message a mistyped path gets, and nothing is bound behind it.</summary>
    [Fact]
    public async Task ResolveAsync_MailServerRefusedTheCreation_ReportsTheRefusalAndRecordsNoBinding()
    {
        // Arrange
        await using var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archive/2026"),
            participation: null,
            mayCreateMissingFolder: true);
        context.RefuseCreationOf(mapping.Alias);

        // Act, Assert
        var refusal = await Assert.ThrowsAsync<RemoteFolderCreationRefusedException>(
            () => context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None));

        Assert.Equal("ARCHIVE", refusal.FolderAlias.Value);
        Assert.DoesNotContain("Archive/2026", refusal.Message, StringComparison.Ordinal);
        await context.PersistenceSessionFactory.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A binding the resolver wrote is a folder tree that moved, so an open client is told to read it again.</summary>
    [Fact]
    public async Task ResolveAsync_RecordingANewBinding_SignalsThatTheAccountsFoldersMoved()
    {
        // Arrange
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act
        await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        context.Clock.Advance(ClientSignals.FoldingWindow);
        await context.Signals.DrainAsync();

        // Assert
        var signal = Assert.Single(context.SignalChannel.Published);
        Assert.Equal(ClientSignalKind.FoldersChanged, signal.Kind);
        Assert.Equal(PrimaryAccount.Id, signal.Account);
        Assert.Null(signal.Folder);
    }

    /// <summary>A resolution that changed no binding is a tree that did not move, and says nothing.</summary>
    [Fact]
    public async Task ResolveAsync_ResolvingTheSameBindingAgain_SignalsOnlyTheFirstTime()
    {
        // Arrange
        await using var context = new ResolverContext(
            new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act
        await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);
        await context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None);

        context.Clock.Advance(ClientSignals.FoldingWindow);
        await context.Signals.DrainAsync();

        // Assert
        Assert.Single(context.SignalChannel.Published);
    }

    /// <summary>Builds a resolver over a server that advertises exactly the folders a test names.</summary>
    private sealed class ResolverContext : IAsyncDisposable
    {
        internal static readonly DateTimeOffset ResolvedAt = new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The delimiter the modelled server reports for every folder it advertises or creates.</summary>
        internal const char AdvertisedDelimiter = '/';

        private readonly Dictionary<string, MailFolderResolution> bindingsByAlias = new(StringComparer.Ordinal);

        internal ResolverContext(params RemoteFolder[] advertisedFolders)
        {
            var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
            remoteFolderCatalog
                .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(advertisedFolders));

            this.ResolutionStore = Substitute.For<IMailFolderResolutionStore>();
            this.ResolutionStore
                .GetCurrentResolutionAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<MailFolderAlias>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(
                    this.bindingsByAlias.GetValueOrDefault(call.Arg<MailFolderAlias>().Value)));

            this.PersistenceSession = Substitute.For<IPersistenceSession>();
            this.PersistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            this.PersistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
            this.PersistenceSessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(this.PersistenceSession);

            this.MappingChangeAuditor = Substitute.For<IMailFolderMappingChangeAuditor>();
            this.MappingChangeAuditor
                .RecordMappingChangeAsync(Arg.Any<MailFolderMappingChange>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.RecordedChanges.Add(call.Arg<MailFolderMappingChange>()!);

                    return Task.CompletedTask;
                });

            this.FolderCreator = Substitute.For<IRemoteFolderCreator>();
            this.FolderCreator
                .CreateFolderAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderAlias>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var configuredPath = call.Arg<RemoteFolderPath>();
                    this.CreatedPaths.Add(configuredPath);

                    // A server answers with the folder as it advertises it, delimiter included, which is exactly what a
                    // later listing would report for the same folder.
                    return Task.FromResult(RemoteFolderPath.Create(configuredPath.Value, AdvertisedDelimiter));
                });

            this.Signals = new ClientSignals([this.SignalChannel], this.Clock);
            this.Resolver = new MailFolderResolver(
                remoteFolderCatalog,
                this.FolderCreator,
                this.ResolutionStore,
                this.MappingChangeAuditor,
                this.PersistenceSessionFactory,
                this.Signals,
                this.Clock);
        }

        /// <summary>Gets the clock the resolver stamps a binding with, and the one the signal window is measured against.</summary>
        internal FakeTimeProvider Clock { get; } = new(ResolvedAt);

        /// <summary>Gets what this arrangement told a client, which most tests here have no claim about.</summary>
        internal RecordingClientSignalChannel SignalChannel { get; } = new();

        /// <summary>Gets the publisher the resolver raises through.</summary>
        internal ClientSignals Signals { get; }

        internal MailFolderResolver Resolver { get; }

        internal IRemoteFolderCreator FolderCreator { get; }

        internal List<RemoteFolderPath> CreatedPaths { get; } = [];

        internal IMailFolderResolutionStore ResolutionStore { get; }

        internal IPersistenceSession PersistenceSession { get; }

        internal IPersistenceSessionFactory PersistenceSessionFactory { get; }

        internal IMailFolderMappingChangeAuditor MappingChangeAuditor { get; }

        internal List<MailFolderMappingChange> RecordedChanges { get; } = [];

        internal void BindAliasTo(MailFolderResolution resolution) =>
            this.bindingsByAlias[resolution.Alias.Value] = resolution;

        /// <summary>Models a mail server that answers the creation of one alias's folder by refusing it.</summary>
        /// <summary>Releases the publisher this context composed, which holds a timer because a channel is registered behind it.</summary>
        /// <returns>A task that completes once the publisher has delivered whatever a window was still holding.</returns>
        public ValueTask DisposeAsync() => this.Signals.DisposeAsync();

        internal void RefuseCreationOf(MailFolderAlias alias) =>
            this.FolderCreator
                .CreateFolderAsync(
                    Arg.Any<MailAccountId>(),
                    alias,
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task<RemoteFolderPath>>(_ =>
                    throw new RemoteFolderCreationRefusedException(PrimaryAccount.Id, alias));
    }
}
