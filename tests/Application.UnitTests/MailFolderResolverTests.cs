// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Folders;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailMcp.Application.UnitTests;

public sealed class MailFolderResolverTests
{
    private static readonly MailAccountId PrimaryAccount = MailAccountId.Create("primary");

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
        var context = new ResolverContext(
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
        var context = new ResolverContext(
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
        var context = new ResolverContext(
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
        var context = new ResolverContext(
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
        var context = new ResolverContext(
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
        var context = new ResolverContext(
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
        var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
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
            Arg.Any<MailAccountId>(),
            Arg.Is<MailFolderResolution>(resolution => resolution!.Alias.Value == "ARCHIVE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_AliasAlreadyBoundToTheAdvertisedFolder_WritesNothingAndRecordsNoChange()
    {
        // Arrange
        var advertisedInbox = RemoteFolderPath.Create("INBOX", '/');
        var context = new ResolverContext(new RemoteFolder(advertisedInbox, [MailFolderSpecialUse.Inbox]));
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
        var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
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
        var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("Archive/2026", '/'), []));
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
        var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("Archive/2026", '/'), []));
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
        var context = new ResolverContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '/'), [MailFolderSpecialUse.Inbox]));
        context.PersistenceSession
            .CommitAsync(Arg.Any<CancellationToken>())
            .Returns(PersistenceCommitResult.ConcurrencyConflict);
        var mapping = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("inbox"), MailFolderSpecialUse.Inbox);

        // Act, Assert
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => context.Resolver.ResolveAsync(PrimaryAccount, mapping, RequiredTlsPolicy, CancellationToken.None));
        await context.MappingChangeAuditor.DidNotReceive().RecordMappingChangeAsync(Arg.Any<MailFolderMappingChange>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Builds a resolver over a server that advertises exactly the folders a test names.</summary>
    private sealed class ResolverContext
    {
        internal static readonly DateTimeOffset ResolvedAt = new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

        private readonly Dictionary<string, MailFolderResolution> bindingsByAlias = new(StringComparer.Ordinal);

        internal ResolverContext(params RemoteFolder[] advertisedFolders)
        {
            var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
            remoteFolderCatalog
                .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(advertisedFolders));

            this.ResolutionStore = Substitute.For<IMailFolderResolutionStore>();
            this.ResolutionStore
                .GetCurrentResolutionAsync(Arg.Any<MailAccountId>(), Arg.Any<MailFolderAlias>(), Arg.Any<CancellationToken>())
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

            this.Resolver = new MailFolderResolver(
                remoteFolderCatalog,
                this.ResolutionStore,
                this.MappingChangeAuditor,
                this.PersistenceSessionFactory,
                new FakeTimeProvider(ResolvedAt));
        }

        internal MailFolderResolver Resolver { get; }

        internal IMailFolderResolutionStore ResolutionStore { get; }

        internal IPersistenceSession PersistenceSession { get; }

        internal IPersistenceSessionFactory PersistenceSessionFactory { get; }

        internal IMailFolderMappingChangeAuditor MappingChangeAuditor { get; }

        internal List<MailFolderMappingChange> RecordedChanges { get; } = [];

        internal void BindAliasTo(MailFolderResolution resolution) =>
            this.bindingsByAlias[resolution.Alias.Value] = resolution;
    }
}
