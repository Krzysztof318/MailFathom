// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Destinations;

/// <summary>Covers how a change's destination becomes a folder, for one the account mirrors and one it only maps.</summary>
public sealed class MailboxDestinationResolverTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("archive");
    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("junk");

    /// <summary>A folder its own run binds is read from that binding, without a word to the mail server.</summary>
    [Fact]
    public async Task ResolveAsync_AMirroredDestination_AnswersFromTheBindingItsRunRecorded()
    {
        // Arrange
        var context = new DestinationContext();
        context.Mappings.With(Account.Id, MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("INBOX/Archive")));
        var binding = context.Bindings.Bind(Account.Id, Archive, "INBOX/Archive");

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToAlias(Archive));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Resolved, resolution.Outcome);
        MailboxDestination destination = resolution.Destination!;
        Assert.Equal(Archive, destination.Alias);
        Assert.Equal(binding.RemotePath, destination.Path);
        Assert.True(destination.IsMirrored);
        Assert.Equal(0, context.ListedFolderCount);
    }

    /// <summary>No run schedules an unmirrored folder, so the moment it is needed as a destination is when it is resolved.</summary>
    [Fact]
    public async Task ResolveAsync_AnUnmirroredDestination_ResolvesItAgainstTheServerAndRecordsTheBinding()
    {
        // Arrange
        var context = new DestinationContext(new RemoteFolder(RemoteFolderPath.Create("INBOX.Spam", '.'), []));
        context.Mappings.With(Account.Id, MappedOnlyPathTo(Junk, "INBOX.Spam"));

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToAlias(Junk));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Resolved, resolution.Outcome);
        Assert.Equal("INBOX.Spam", resolution.Destination!.Path.Value);
        Assert.False(resolution.Destination.IsMirrored);
        Assert.NotNull(await context.Bindings.GetCurrentResolutionAsync(Account, Junk, TestContext.Current.CancellationToken));
        Assert.Equal(Junk, Assert.Single(context.RecordedChanges).Alias);
    }

    /// <summary>A batch filing two hundred messages into one folder must cost one listing, not two hundred.</summary>
    [Fact]
    public async Task ResolveAsync_TheSameUnmirroredDestinationTwice_ListsTheServerOnce()
    {
        // Arrange
        var context = new DestinationContext(new RemoteFolder(RemoteFolderPath.Create("INBOX.Spam", '.'), []));
        context.Mappings.With(Account.Id, MappedOnlyPathTo(Junk, "INBOX.Spam"));

        // Act
        await context.ResolveAsync(MailFolderReference.ToAlias(Junk));
        var second = await context.ResolveAsync(MailFolderReference.ToAlias(Junk));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Resolved, second.Outcome);
        Assert.Equal(1, context.ListedFolderCount);
    }

    /// <summary>A server that moved the folder is followed, exactly as a mirrored folder's next run follows it.</summary>
    [Fact]
    public async Task ResolveAsync_AnUnmirroredDestinationTheServerRenamed_BindsTheNextGenerationToTheNewFolder()
    {
        // Arrange
        var context = new DestinationContext(new RemoteFolder(RemoteFolderPath.Create("INBOX.Junk", '.'), []));
        context.Mappings.With(Account.Id, MappedOnlyPathTo(Junk, "INBOX.Junk"));
        var previous = context.Bindings.Bind(Account.Id, Junk, "INBOX.Spam");

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToAlias(Junk));

        // Assert
        Assert.Equal("INBOX.Junk", resolution.Destination!.Path.Value);
        var replaced = await context.Bindings.GetCurrentResolutionAsync(Account, Junk, TestContext.Current.CancellationToken);
        Assert.Equal(previous.Generation.Value + 1, replaced!.Generation.Value);
    }

    /// <summary>Mapping the folder is the whole of what makes it reachable, so an alias no mapping declares reaches nothing.</summary>
    [Fact]
    public async Task ResolveAsync_AnAliasNoMappingDeclares_ReportsItAsUnmapped()
    {
        // Arrange
        var context = new DestinationContext();
        context.Bindings.Bind(Account.Id, Archive, "INBOX/Archive");

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToAlias(Archive));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Unmapped, resolution.Outcome);
        Assert.Null(resolution.Destination);
    }

    /// <summary>A role is a question only configuration answers, so one no folder of the account plays names nothing.</summary>
    [Fact]
    public async Task ResolveAsync_ARoleTheAccountMapsNoFolderFor_ReportsItAsUnmapped()
    {
        // Arrange
        var context = new DestinationContext();

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Unmapped, resolution.Outcome);
    }

    /// <summary>Falling back to the configured path would file mail into a folder the server never said it had.</summary>
    [Fact]
    public async Task ResolveAsync_AnUnmirroredDestinationTheServerDoesNotAdvertise_ReportsItAsNotAdvertised()
    {
        // Arrange
        var context = new DestinationContext(new RemoteFolder(RemoteFolderPath.Create("INBOX", '.'), []));
        context.Mappings.With(Account.Id, MappedOnlyPathTo(Junk, "INBOX.Spam"));

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToAlias(Junk));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.NotAdvertised, resolution.Outcome);
        Assert.Null(resolution.Destination);
    }

    /// <summary>Which of two folders carrying one role was meant is the operator's to state, never this resolver's to pick.</summary>
    [Fact]
    public async Task ResolveAsync_ARoleTwoAdvertisedFoldersCarry_ReportsItAsAmbiguous()
    {
        // Arrange
        var context = new DestinationContext(
            new RemoteFolder(RemoteFolderPath.Create("INBOX.Spam", '.'), [MailFolderSpecialUse.Junk]),
            new RemoteFolder(RemoteFolderPath.Create("INBOX.Junk", '.'), [MailFolderSpecialUse.Junk]));
        context.Mappings.With(
            Account.Id,
            MailFolderMapping.ToSpecialUse(Junk, MailFolderSpecialUse.Junk, MailFolderParticipation.MappedOnly));

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Ambiguous, resolution.Outcome);
    }

    /// <summary>A mirrored folder whose run has not bound it yet waits for that run rather than being listed here.</summary>
    [Fact]
    public async Task ResolveAsync_AMirroredDestinationNothingHasBound_ReportsItAsUnbound()
    {
        // Arrange
        var context = new DestinationContext(new RemoteFolder(RemoteFolderPath.Create("INBOX/Archive", '/'), []));
        context.Mappings.With(Account.Id, MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create("INBOX/Archive")));

        // Act
        var resolution = await context.ResolveAsync(MailFolderReference.ToAlias(Archive));

        // Assert
        Assert.Equal(MailboxDestinationOutcome.Unbound, resolution.Outcome);
        Assert.Equal(0, context.ListedFolderCount);
    }

    /// <summary>An account whose rules only flag or delete mail must never reach its mail server from here.</summary>
    [Fact]
    public async Task ResolveAsync_NoDestinationNamed_AsksNothingAndAnswersEveryLookupAsUnbound()
    {
        // Arrange
        var context = new DestinationContext();

        // Act
        MailboxDestinations destinations = await context.Resolver.ResolveAsync(
            Account,
            [],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, context.ListedFolderCount);
        Assert.Equal(
            MailboxDestinationOutcome.Unbound,
            destinations.Find(MailFolderReference.ToAlias(Archive)).Outcome);
    }

    /// <summary>A folder MailFathom knows by name and mirrors nothing of, which is what this issue's destination is.</summary>
    private static MailFolderMapping MappedOnlyPathTo(MailFolderAlias alias, string remotePath) =>
        MailFolderMapping.ToRemotePath(
            alias,
            RemoteFolderPath.Create(remotePath),
            MailFolderParticipation.MappedOnly);

    /// <summary>The collaborators one resolution needs: a server that advertises folders and a store that keeps bindings.</summary>
    private sealed class DestinationContext
    {
        private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.TlsOnConnect,
            MailAuthenticationPolicy.Create(
                [MailAuthenticationMechanism.Plain],
                allowInsecureConnection: false,
                allowClearTextAuthenticationOverUnencryptedConnection: false),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);

        internal DestinationContext(params RemoteFolder[] advertisedFolders)
        {
            var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
            remoteFolderCatalog
                .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    this.ListedFolderCount++;

                    return Task.FromResult<IReadOnlyList<RemoteFolder>>(advertisedFolders);
                });

            var persistenceSession = Substitute.For<IPersistenceSession>();
            persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
            persistenceSessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

            var mappingChangeAuditor = Substitute.For<IMailFolderMappingChangeAuditor>();
            mappingChangeAuditor
                .RecordMappingChangeAsync(Arg.Any<MailFolderMappingChange>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.RecordedChanges.Add(call.Arg<MailFolderMappingChange>()!);

                    return Task.CompletedTask;
                });

            var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);

            this.Resolver = new MailboxDestinationResolver(
                this.Mappings.Resolver,
                this.Bindings,
                new MailFolderResolver(
                    remoteFolderCatalog,
                    Substitute.For<IRemoteFolderCreator>(),
                    this.Bindings,
                    mappingChangeAuditor,
                    persistenceSessionFactory,
                    new FakeTimeProvider(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero))),
                transportSecurityPolicies);
        }

        internal StubMailFolderMappings Mappings { get; } = StubMailFolderMappings.Nothing;

        internal InMemoryMailFolderResolutionStore Bindings { get; } = new();

        internal MailboxDestinationResolver Resolver { get; }

        internal List<MailFolderMappingChange> RecordedChanges { get; } = [];

        internal int ListedFolderCount { get; private set; }

        internal async Task<MailboxDestinationResolution> ResolveAsync(MailFolderReference destination)
        {
            var destinations = await this.Resolver.ResolveAsync(
                Account,
                [destination],
                TestContext.Current.CancellationToken);

            return destinations.Find(destination);
        }
    }
}
