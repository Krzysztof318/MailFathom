// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Drain;

public sealed class MailboxDrainPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX"));

    private static readonly MailFolderResolution Archive = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("archive"),
        RemoteFolderPath.Create("Archive"));

    private static readonly DateTimeOffset RunInstant = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TransportPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    [Fact]
    public async Task DrainAsync_AccountMirrorsItsSource_IssuesNothingAndRemovesNothing()
    {
        // Arrange
        var context = new DrainContext(MailAccountCustodyState.Mirrored)
            .Storing(Stored(Inbox, uid: 11));

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.DrainedCount);
        Assert.Empty(context.Store.Cleared);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainAsync_MessageIsStoredAndReadsBackIntact_ExpungesExactlyThatUidAndClearsTheOccurrence()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11));

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DrainedCount);
        Assert.Equal([ImapUid.Create(11)], context.ExpungedUids);
        Assert.Equal([ImapUidValidity.Create(1)], context.ExpungedUidValidities);
        Assert.Single(context.Store.Cleared);
    }

    [Fact]
    public async Task DrainAsync_PayloadWasNeverStored_LeavesTheMessageOnItsSourceAndCountsWhy()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11) with
            {
                ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
            });

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.DrainedCount);
        Assert.Equal(1, report.HeldBack[MailboxDrainHoldBack.ContentAboveSizeLimit]);
        Assert.Empty(context.ExpungedUids);
    }

    [Fact]
    public async Task DrainAsync_StoredPayloadDoesNotMatchWhatTheRowRecords_LeavesTheMessageOnItsSource()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11))
            .WithDamagedPayload();

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.DrainedCount);
        Assert.Equal(1, report.HeldBack[MailboxDrainHoldBack.ContentDoesNotMatchRecord]);
        Assert.Empty(context.ExpungedUids);
        Assert.Empty(context.Store.Verified);
    }

    [Fact]
    public async Task DrainAsync_NoPayloadIsStoredAtAll_LeavesTheMessageOnItsSource()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11))
            .WithNoStoredPayload();

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.HeldBack[MailboxDrainHoldBack.ContentNotStored]);
        Assert.Empty(context.ExpungedUids);
    }

    [Fact]
    public async Task DrainAsync_RowChangedBetweenTheSelectionAndTheCommand_DropsItFromTheBatch()
    {
        // Arrange
        var erasedSince = Stored(Inbox, uid: 11);
        var stillThere = Stored(Inbox, uid: 12);
        var context = new DrainContext(Held).Storing(erasedSince, stillThere);

        context.Store.ReReadAnswer = [stillThere with { ContentVerifiedAt = RunInstant }];

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DrainedCount);
        Assert.Equal([ImapUid.Create(12)], context.ExpungedUids);
    }

    [Fact]
    public async Task DrainAsync_MoreMessagesThanOneCommandMayName_SplitsThemIntoBoundedBatches()
    {
        // Arrange
        var context = new DrainContext(Held, maxPerCommand: 2)
            .Storing(Stored(Inbox, uid: 11), Stored(Inbox, uid: 12), Stored(Inbox, uid: 13));

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, report.DrainedCount);
        Assert.Equal(2, context.IssuedBatchSizes.Count);
        Assert.Equal([2, 1], context.IssuedBatchSizes);
    }

    [Fact]
    public async Task DrainAsync_MessagesLieInSeveralFolders_OpensOneWriteSessionPerFolder()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11), Stored(Archive, uid: 21));

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.DrainedCount);
        Assert.Equal([Inbox, Archive], context.OpenedFolders);
    }

    [Fact]
    public async Task DrainAsync_FolderReportsAnotherUidValidity_AbandonsTheBatchAndClearsNothing()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11))
            .WithFolderRecreated();

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.DrainedCount);
        Assert.Equal(1, report.AbandonedBatchCount);
        Assert.Empty(context.Store.Cleared);
    }

    [Fact]
    public async Task DrainAsync_SourceRefusesTheCommand_CountsAFailedBatchAndLeavesTheOccurrenceStanding()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11))
            .WithUnreachableSource();

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.FailedBatchCount);
        Assert.True(report.Failed);
        Assert.Empty(context.Store.Cleared);
    }

    [Fact]
    public async Task DrainAsync_MessageWasErasedBeforeTheDrainReachedIt_RemovesItsSourceCopyAndDeletesTheRecord()
    {
        // Arrange
        var removal = new MailboxSourceRemoval(
            MailboxSourceRemovalId.New(),
            EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(1), ImapUid.Create(31)),
            Inbox);

        var context = new DrainContext(Held).AwaitingRemovalOf(removal);

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.RemovedErasedCount);
        Assert.Equal([ImapUid.Create(31)], context.ExpungedUids);
        Assert.Equal([removal.Id], context.Store.DeletedRemovals);
    }

    [Fact]
    public async Task DrainAsync_ConfigurationHasComeToSynchronizeAVirtualFolder_PausesTheDrain()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11))
            .SynchronizingAVirtualFolder();

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.DrainedCount);
        Assert.Empty(context.ExpungedUids);
    }

    [Fact]
    public async Task DrainAsync_HoldingWasAskedForAndNothingIsOutstanding_MovesTheAccountToHeldAndDrains()
    {
        // Arrange
        var context = new DrainContext(
                new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Mirrored))
            .Storing(Stored(Inbox, uid: 11));

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailAccountCustodyPhase.Held, context.Custody.StateOf(Account)!.Phase);
        Assert.Equal(1, report.DrainedCount);
    }

    [Fact]
    public async Task DrainAsync_HoldingWasAskedForWhileAChangeIsOutstanding_LeavesTheAccountMirrored()
    {
        // Arrange
        var context = new DrainContext(
                new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Mirrored))
            .Storing(Stored(Inbox, uid: 11))
            .WithAnOutstandingMutation();

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailAccountCustodyPhase.Mirrored, context.Custody.StateOf(Account)!.Phase);
        Assert.Equal(0, report.DrainedCount);
    }

    [Fact]
    public async Task DrainAsync_MirroringWasAskedForOnAHeldAccount_MovesItToRestoringAndDrainsNothingFurther()
    {
        // Arrange
        var context = new DrainContext(
                new MailAccountCustodyState(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Held))
            .Storing(Stored(Inbox, uid: 11));

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailAccountCustodyPhase.Restoring, context.Custody.StateOf(Account)!.Phase);
        Assert.Equal(0, report.DrainedCount);
    }

    [Fact]
    public async Task DrainAsync_PayloadWasAlreadyReadBackOnAnEarlierPass_DoesNotReadItAgain()
    {
        // Arrange
        var context = new DrainContext(Held)
            .Storing(Stored(Inbox, uid: 11) with { ContentVerifiedAt = RunInstant });

        // Act
        var report = await context.Pass.DrainAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.DrainedCount);
        Assert.Empty(context.Store.Verified);
        await context.Content.DidNotReceive().FindStoredContentAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
    }

    private static MailAccountCustodyState Held { get; } =
        new(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held);

    private static MailboxDrainCandidate Stored(MailFolderResolution folder, uint uid) => new(
        StoredEmailId.Create(Guid.CreateVersion7()),
        EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(1), ImapUid.Create(uid)),
        folder,
        StoredEmailContentAvailability.Available,
        ContentVerifiedAt: null);

    private sealed class DrainContext
    {
        private static readonly byte[] Payload = Encoding.ASCII.GetBytes("Subject: stored\r\n\r\nbody\r\n");

        private readonly IMailFolderMappingReader mappings = Substitute.For<IMailFolderMappingReader>();
        private readonly IMailboxMutationRecordStore mutations = Substitute.For<IMailboxMutationRecordStore>();

        internal DrainContext(MailAccountCustodyState custody, int maxPerCommand = 50)
        {
            this.Custody = InMemoryMailAccountCustodyStore.With(Account, custody);

            var persistenceSession = Substitute.For<IPersistenceSession>();
            persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

            this.mappings.FoldersOf(Account).Returns([
                MailFolderMapping.ToRemotePath(Inbox.Alias, Inbox.RemotePath),
            ]);
            this.mutations.ReadOutstandingAsync(Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns([]);

            this.Content = Substitute.For<IEmailContentStore>();
            this.Content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(Payload, Payload.Length, SHA256.HashData(Payload)));

            this.WriteSession = Substitute.For<IMailboxWriteSession>();
            this.WriteSession
                .When(session => session.ExpungeDrainedAsync(
                    Arg.Any<ImapUidValidity>(),
                    Arg.Any<IReadOnlyCollection<ImapUid>>(),
                    Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    var uids = call.ArgAt<IReadOnlyCollection<ImapUid>>(1);

                    this.ExpungedUidValidities.Add(call.ArgAt<ImapUidValidity>(0));
                    this.ExpungedUids.AddRange(uids);
                    this.IssuedBatchSizes.Add(uids.Count);
                });

            this.WriteSessionFactory = Substitute.For<IMailboxWriteSessionFactory>();
            this.WriteSessionFactory.OpenForWritingAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderResolution>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.OpenedFolders.Add(call.ArgAt<MailFolderResolution>(1));

                    return this.WriteSession;
                });

            var transportSecurity = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportSecurity.GetPolicy(Account).Returns(TransportPolicy);

            this.Pass = new MailboxDrainPass(
                this.Custody,
                this.Store,
                this.mutations,
                this.Content,
                this.WriteSessionFactory,
                transportSecurity,
                this.mappings,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                    TimeProvider.System),
                new MailboxSynchronizationOptions
                {
                    MaxDrainedEmailsPerRun = 200,
                    MaxDrainedEmailsPerCommand = maxPerCommand,
                },
                new FakeTimeProvider(RunInstant));
        }

        internal InMemoryMailAccountCustodyStore Custody { get; }

        internal InMemoryMailboxDrainStore Store { get; } = new();

        internal IEmailContentStore Content { get; }

        internal IMailboxWriteSession WriteSession { get; }

        internal IMailboxWriteSessionFactory WriteSessionFactory { get; }

        internal MailboxDrainPass Pass { get; }

        internal List<ImapUid> ExpungedUids { get; } = [];

        internal List<ImapUidValidity> ExpungedUidValidities { get; } = [];

        internal List<int> IssuedBatchSizes { get; } = [];

        internal List<MailFolderResolution> OpenedFolders { get; } = [];

        internal DrainContext Storing(params MailboxDrainCandidate[] held)
        {
            this.Store.Holding(held);

            return this;
        }

        internal DrainContext AwaitingRemovalOf(params MailboxSourceRemoval[] awaiting)
        {
            this.Store.AwaitingRemovalOf(awaiting);

            return this;
        }

        internal DrainContext WithDamagedPayload()
        {
            this.Content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(Payload, Payload.Length + 1, SHA256.HashData(Payload)));

            return this;
        }

        internal DrainContext WithNoStoredPayload()
        {
            this.Content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
                .Returns((StoredEmailContent?)null);

            return this;
        }

        internal DrainContext WithFolderRecreated()
        {
            this.WriteSession.ExpungeDrainedAsync(
                    Arg.Any<ImapUidValidity>(),
                    Arg.Any<IReadOnlyCollection<ImapUid>>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new MailboxFolderRecreatedException(
                    Account,
                    Inbox.Alias,
                    ImapUidValidity.Create(1),
                    ImapUidValidity.Create(2)));

            return this;
        }

        internal DrainContext WithUnreachableSource()
        {
            this.WriteSession.ExpungeDrainedAsync(
                    Arg.Any<ImapUidValidity>(),
                    Arg.Any<IReadOnlyCollection<ImapUid>>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new MailboxUnavailableException(
                    Account,
                    Inbox.Alias,
                    new TimeoutException("The source did not answer.")));

            return this;
        }

        internal DrainContext SynchronizingAVirtualFolder()
        {
            this.mappings.FoldersOf(Account).Returns([
                MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("all"), MailFolderSpecialUse.All),
            ]);

            return this;
        }

        internal DrainContext WithAnOutstandingMutation()
        {
            var record = new MailboxMutationRecord
            {
                Id = MailboxMutationRecordId.Create(Guid.CreateVersion7()),
                Request = MailboxMutationRequest.Delete(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(1), ImapUid.Create(99)),
                    MailboxMutationRequester.Create(MailboxMutationOrigin.Command, "somebody"),
                    AuthoredDeleteEmailDisposition.RetainLocalCopy),
                Stage = MailboxMutationStage.Recorded,
                IsAudited = false,
                RequiresSourceRemoval = false,
                Placement = RemoteEmailPlacement.NotReported(),
                AttemptCount = 0,
                RecordedAt = RunInstant,
                StageChangedAt = RunInstant,
                LastFailure = null,
                PlacementObservedAt = null,
                SourceRemovalObservedAt = null,
            };

            this.mutations.ReadOutstandingAsync(Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns([new OutstandingMailboxMutation(record, Inbox)]);

            return this;
        }
    }
}
